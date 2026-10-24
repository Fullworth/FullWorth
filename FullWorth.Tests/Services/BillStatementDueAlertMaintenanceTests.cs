using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillStatementDueAlertMaintenanceTests
{
    private static FullWorthDbContext Database() => new(
        new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase($"due-maintenance-{Guid.NewGuid():N}").Options);

    private static BillStatementDueAlertMaintenance Service(FullWorthDbContext db)
    {
        var streams = new BillStreamReadGateway(db);
        return new BillStatementDueAlertMaintenance(db,
            new BillDueReminderMaintenanceReadGateway(db),
            new BillStatementPaymentDueAlertService(
                streams, new BillAlertReconciliationGateway(db)));
    }

    [Fact]
    public async Task ExistingReminder_BecomesUrgentThenExpiresWithoutNewUpload()
    {
        await using var db = Database();
        var owner = Guid.NewGuid();
        var stream = new BillStreamEntity
        {
            UserId = owner, ProviderName = "Synthetic recurring utility", IsActive = true
        };
        db.BillStreams.Add(stream);
        var due = new DateOnly(2026, 11, 15);
        db.BillStatements.Add(new BillStatementEntity
        {
            UserId = owner, BillStreamId = stream.Id,
            PeriodStart = new DateOnly(2026, 10, 1),
            PeriodEnd = new DateOnly(2026, 10, 31),
            DueDate = due, TotalAmount = 105m, CurrencyCode = "USD"
        });
        await db.SaveChangesAsync();
        var alerts = new BillStatementPaymentDueAlertService(
            new BillStreamReadGateway(db), new BillAlertReconciliationGateway(db));
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        await alerts.ReconcileAsync(owner, stream.Id, due, 105m, "USD",
            new DateOnly(2026, 11, 1), now);
        await db.SaveChangesAsync();
        var initial = Assert.Single(await db.BillAlerts.ToListAsync());
        Assert.Equal(BillAlertSeverity.Info, initial.Severity);

        var service = Service(db);
        await service.ReconcileOneAsync(owner, stream.Id,
            new DateOnly(2026, 11, 10), now.AddDays(9));
        var warning = Assert.Single(await db.BillAlerts.ToListAsync());
        Assert.Equal(initial.Id, warning.Id);
        Assert.Equal(BillAlertSeverity.Warning, warning.Severity);

        await service.ReconcileOneAsync(owner, stream.Id, due, now.AddDays(14));
        Assert.Single(await db.BillAlerts.ToListAsync()); // Due today is still relevant.
        await service.ReconcileOneAsync(owner, stream.Id, due.AddDays(1), now.AddDays(15));
        Assert.Empty(await db.BillAlerts.ToListAsync());
    }

    [Fact]
    public async Task InactiveOrForeignStream_CannotGenerateNewDueAlert()
    {
        await using var db = Database();
        var owner = Guid.NewGuid();
        var stream = new BillStreamEntity
        {
            UserId = owner, ProviderName = "Synthetic provider", IsActive = false
        };
        db.BillStreams.Add(stream);
        db.BillStatements.Add(new BillStatementEntity
        {
            UserId = owner, BillStreamId = stream.Id,
            PeriodStart = new DateOnly(2026, 10, 1),
            PeriodEnd = new DateOnly(2026, 10, 31),
            DueDate = new DateOnly(2026, 11, 20),
            TotalAmount = 100m, CurrencyCode = "USD"
        });
        await db.SaveChangesAsync();

        var service = Service(db);
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        await service.ReconcileOneAsync(owner, stream.Id, new DateOnly(2026, 11, 1), now);
        await service.ReconcileOneAsync(Guid.NewGuid(), stream.Id, new DateOnly(2026, 11, 1), now);
        Assert.Empty(await db.BillAlerts.ToListAsync());
    }

    [Fact]
    public async Task RepeatedMaintenanceKeepsSameReminderIdentityAndUserScope()
    {
        await using var db = Database();
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var stream = new BillStreamEntity
        {
            UserId = owner, ProviderName = "Synthetic recurring provider", IsActive = true
        };
        db.BillStreams.Add(stream);
        db.BillStatements.Add(new BillStatementEntity
        {
            UserId = owner, BillStreamId = stream.Id,
            PeriodStart = new DateOnly(2026, 10, 1),
            PeriodEnd = new DateOnly(2026, 10, 31),
            DueDate = new DateOnly(2026, 11, 20),
            TotalAmount = 90m, CurrencyCode = "USD"
        });
        db.BillAlerts.Add(new BillAlertEntity
        {
            UserId = otherOwner, BillStreamId = stream.Id,
            AlertType = BillAlertType.PaymentDue,
            Title = "Other owner reminder", Message = "Do not modify"
        });
        await db.SaveChangesAsync();

        var service = Service(db);
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
        await service.ReconcileOneAsync(owner, stream.Id, new DateOnly(2026, 11, 1), now);
        var first = Assert.Single(await db.BillAlerts.Where(a => a.UserId == owner).ToListAsync());
        await service.ReconcileOneAsync(owner, stream.Id, new DateOnly(2026, 11, 1), now);
        var second = Assert.Single(await db.BillAlerts.Where(a => a.UserId == owner).ToListAsync());
        Assert.Equal(first.Id, second.Id);
        var foreign = Assert.Single(await db.BillAlerts.Where(a => a.UserId == otherOwner).ToListAsync());
        Assert.Equal("Other owner reminder", foreign.Title);
    }
}

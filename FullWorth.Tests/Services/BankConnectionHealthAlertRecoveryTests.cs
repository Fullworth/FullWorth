using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FullWorth.Tests.Services;

public sealed class BankConnectionHealthAlertRecoveryTests
{
    private const string Title = "Bank connection needs attention";
    private const string Message = "Synthetic Bank needs attention before FullWorth can continue reliable bank monitoring. Review or reconnect it in Connect.";
    private static readonly DateTimeOffset Old = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Theory]
    [InlineData("create", false, false)]
    [InlineData("create", true, false)]
    [InlineData("create", false, true)]
    [InlineData("create", true, true)]
    [InlineData("update", false, false)]
    [InlineData("update", true, false)]
    [InlineData("update", false, true)]
    [InlineData("update", true, true)]
    [InlineData("dedupe", false, false)]
    [InlineData("dedupe", true, false)]
    [InlineData("dedupe", false, true)]
    [InlineData("dedupe", true, true)]
    [InlineData("remove", false, false)]
    [InlineData("remove", true, false)]
    [InlineData("remove", false, true)]
    [InlineData("remove", true, true)]
    public async Task FailedSave_RestoresOnlyOwnedAlertMutations_ThenRetrySucceeds(
        string operation, bool callerEdits, bool cancel)
    {
        var failure = new Failure();
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        db.BankConnections.Add(new BankConnectionEntity
        {
            UserId = owner, InstitutionName = "Synthetic Bank",
            Status = operation == "remove" ? BankConnectionStatus.Active : BankConnectionStatus.RequiresAttention
        });
        var primary = Alert(owner, operation == "dedupe" ? Message : "Old warning");
        var duplicate = Alert(owner, "Duplicate warning"); duplicate.CreatedAtUtc = Old.AddMinutes(1);
        var foreign = Alert(other, "Foreign warning");
        var billSpecific = Alert(owner, "Bill-specific warning"); billSpecific.BillStreamId = Guid.NewGuid();
        var callerModified = Alert(owner, "Caller original"); callerModified.Title = "Different alert";
        var callerDeleted = Alert(owner, "Caller deletion"); callerDeleted.Title = "Different alert";
        if (operation != "create") db.BillAlerts.Add(primary);
        if (operation is "dedupe" or "remove") db.BillAlerts.Add(duplicate);
        db.BillAlerts.AddRange(foreign, billSpecific, callerModified, callerDeleted);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var pending = Alert(owner, "Caller insertion"); pending.Title = "Different alert";
        db.BillAlerts.Add(pending);
        callerModified = await db.BillAlerts.SingleAsync(a => a.Id == callerModified.Id);
        callerModified.Message = "Caller pending";
        callerDeleted = await db.BillAlerts.SingleAsync(a => a.Id == callerDeleted.Id);
        db.BillAlerts.Remove(callerDeleted);
        if (callerEdits && operation != "create")
        {
            primary = await db.BillAlerts.SingleAsync(a => a.Id == primary.Id);
            primary.Message = "Caller pending health message";
        }
        db.ChangeTracker.DetectChanges();
        var expected = await db.BillAlerts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => Row(a));
        if (callerEdits && operation != "create") expected[primary.Id] = Row(primary);
        var pendingEntry = db.Entry(pending);
        var modifiedEntry = db.Entry(callerModified);
        var deletedEntry = db.Entry(callerDeleted);
        var modifiedProperties = modifiedEntry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).ToArray();

        using var cancellation = new CancellationTokenSource();
        failure.Armed = true;
        failure.Cancel = cancel ? cancellation.Cancel : null;
        var service = new BankConnectionHealthAlertService(db, new PlaidBankConnectionReadGateway(db));
        var error = await Record.ExceptionAsync(() => service.ReconcileAsync(owner, cancellation.Token));
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<DbUpdateException>(error);

        Assert.Equal(EntityState.Added, pendingEntry.State);
        Assert.Equal(EntityState.Modified, modifiedEntry.State);
        Assert.Equal(EntityState.Deleted, deletedEntry.State);
        Assert.Equal("Caller pending", callerModified.Message);
        Assert.Equal("Caller original", modifiedEntry.OriginalValues.GetValue<string>(nameof(BillAlertEntity.Message)));
        Assert.Equal(modifiedProperties, modifiedEntry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name));
        var trackedHealth = db.ChangeTracker.Entries<BillAlertEntity>()
            .Where(e => e.Entity.UserId == owner && e.Entity.Title == Title && e.Entity.BillStreamId == null).ToList();
        Assert.Equal(operation == "create" ? 0 : operation == "update" ? 1 : 2, trackedHealth.Count);
        foreach (var entry in trackedHealth)
        {
            Assert.Equal(expected[entry.Entity.Id], Row(entry.Entity));
            var edited = callerEdits && entry.Entity.Id == primary.Id;
            Assert.Equal(edited ? EntityState.Modified : EntityState.Unchanged, entry.State);
            Assert.Equal(edited ? new[] { nameof(BillAlertEntity.Message) } : Array.Empty<string>(),
                entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name));
            if (edited)
                Assert.Equal(operation == "dedupe" ? Message : "Old warning",
                    entry.OriginalValues.GetValue<string>(nameof(BillAlertEntity.Message)));
        }

        // A later caller save must not accidentally commit the failed alert operation.
        await db.SaveChangesAsync();
        await using (var verification = new FullWorthDbContext(options))
        {
            var alerts = await verification.BillAlerts.AsNoTracking().ToDictionaryAsync(a => a.Id, a => Row(a));
            Assert.Equal(expected[foreign.Id], alerts[foreign.Id]);
            Assert.Equal(expected[billSpecific.Id], alerts[billSpecific.Id]);
            if (operation != "create") Assert.Equal(expected[primary.Id], alerts[primary.Id]);
            if (operation is "dedupe" or "remove") Assert.Equal(expected[duplicate.Id], alerts[duplicate.Id]);
            Assert.Equal(operation == "create" ? 0 : operation == "update" ? 1 : 2,
                await verification.BillAlerts.CountAsync(a => a.UserId == owner && a.Title == Title && a.BillStreamId == null));
            Assert.True(alerts.ContainsKey(pending.Id));
            Assert.False(alerts.ContainsKey(callerDeleted.Id));
            Assert.Equal("Caller pending", (await verification.BillAlerts.SingleAsync(a => a.Id == callerModified.Id)).Message);
        }

        await service.ReconcileAsync(owner);
        await using var final = new FullWorthDbContext(options);
        var finalHealth = await final.BillAlerts.Where(a => a.UserId == owner && a.Title == Title && a.BillStreamId == null).ToListAsync();
        if (operation == "remove") Assert.Empty(finalHealth);
        else
        {
            var alert = Assert.Single(finalHealth);
            Assert.Equal(Message, alert.Message);
            Assert.Equal(BillAlertSeverity.Warning, alert.Severity);
            if (operation != "dedupe" || callerEdits)
            {
                Assert.False(alert.IsRead);
                Assert.False(alert.IsDismissed);
            }
            else
            {
                Assert.True(alert.IsRead);
                Assert.True(alert.IsDismissed);
                Assert.Equal(Old, alert.UpdatedAtUtc);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnchangedHealth_DoesNotSaveOrResetAcknowledgement_WhenCallerHasPendingWork(bool attention)
    {
        var failure = new Failure();
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid();
        db.BankConnections.Add(new BankConnectionEntity
        {
            UserId = owner, InstitutionName = "Synthetic Bank",
            Status = attention ? BankConnectionStatus.RequiresAttention : BankConnectionStatus.Active
        });
        if (attention) db.BillAlerts.Add(Alert(owner, Message));
        await db.SaveChangesAsync();
        var before = failure.Saves;
        var pending = Alert(owner, "Caller insertion"); pending.Title = "Other pending work";
        db.BillAlerts.Add(pending);

        await new BankConnectionHealthAlertService(db, new PlaidBankConnectionReadGateway(db)).ReconcileAsync(owner);

        Assert.Equal(before, failure.Saves);
        Assert.Equal(EntityState.Added, db.Entry(pending).State);
        await using var verification = new FullWorthDbContext(options);
        Assert.False(await verification.BillAlerts.AnyAsync(a => a.Id == pending.Id));
        if (attention)
        {
            var alert = await verification.BillAlerts.SingleAsync();
            Assert.True(alert.IsRead);
            Assert.True(alert.IsDismissed);
            Assert.Equal(Old, alert.UpdatedAtUtc);
        }
    }

    private static BillAlertEntity Alert(Guid owner, string message) => new()
    {
        UserId = owner, Title = Title, Message = message, AlertType = BillAlertType.ConnectionIssue,
        Severity = BillAlertSeverity.Warning, IsRead = true, IsDismissed = true,
        CreatedAtUtc = Old, UpdatedAtUtc = Old
    };

    private static (Guid Owner, Guid? Bill, Guid? Change, BillAlertType Type, BillAlertSeverity Severity,
        string Title, string Message, bool Read, bool Dismissed, DateTimeOffset Created, DateTimeOffset Updated)
        Row(BillAlertEntity alert) => (alert.UserId, alert.BillStreamId, alert.BillChangeId, alert.AlertType,
            alert.Severity, alert.Title, alert.Message, alert.IsRead, alert.IsDismissed, alert.CreatedAtUtc, alert.UpdatedAtUtc);

    private sealed class Failure : SaveChangesInterceptor
    {
        public int Saves { get; private set; }
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Saves++;
            if (Armed)
            {
                Armed = false;
                if (Cancel is not null)
                {
                    Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                throw new DbUpdateException("Synthetic pre-commit alert failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FullWorth.Tests.Services;

public sealed class StatementReminderReliabilityBatchTests
{
    private static readonly DateOnly Today = new(2026, 11, 1);
    private static readonly DateTimeOffset Old = DateTimeOffset.Parse("2026-09-01T00:00:00Z");

    [Theory]
    [InlineData("negative")]
    [InlineData("invalid-currency")]
    [InlineData("blank-currency")]
    [InlineData("missing-statement")]
    [InlineData("missing-date")]
    [InlineData("past")]
    [InlineData("zero")]
    [InlineData("inactive")]
    public async Task UnusableLatestEvidence_ClearsOnlyOwnedSlot_WithoutOlderFallbackOrFactMutation(string reason)
    {
        await using var db = Db();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var bill = Bill(owner);
        var foreignBill = Bill(other);
        if (reason == "inactive") { bill.IsActive = false; bill.ProviderName = ""; }
        var old = Alert(owner, bill.Id, BillAlertType.PaymentDue);
        var fee = Alert(owner, bill.Id, BillAlertType.NewFee);
        var foreign = Alert(other, foreignBill.Id, BillAlertType.PaymentDue);
        db.AddRange(bill, foreignBill, old, fee, foreign);
        BillStatementEntity? latest = null;
        if (reason != "missing-statement")
        {
            var earlier = Statement(owner, bill.Id);
            earlier.PeriodStart = new DateOnly(2026, 9, 1);
            earlier.PeriodEnd = new DateOnly(2026, 9, 30);
            db.Add(earlier);
            latest = Statement(owner, bill.Id);
            if (reason == "negative") latest.TotalAmount = -1m;
            if (reason == "invalid-currency") latest.CurrencyCode = "U$D";
            if (reason == "blank-currency") latest.CurrencyCode = "";
            if (reason == "missing-date") latest.DueDate = null;
            if (reason == "past") latest.DueDate = Today.AddDays(-1);
            if (reason == "zero") latest.TotalAmount = 0m;
            if (reason == "inactive") { latest.TotalAmount = -1m; latest.CurrencyCode = ""; }
            db.Add(latest);
        }
        await db.SaveChangesAsync();
        var facts = await db.BillStatements.AsNoTracking().ToDictionaryAsync(s => s.Id,
            s => new { s.TotalAmount, s.CurrencyCode, s.DueDate, s.PeriodStart, s.PeriodEnd });
        await Maintenance(db).ReconcileOneAsync(owner, bill.Id, Today, DateTimeOffset.UtcNow);
        Assert.False(await db.BillAlerts.AnyAsync(a => a.Id == old.Id));
        Assert.True(await db.BillAlerts.AnyAsync(a => a.Id == fee.Id));
        Assert.True(await db.BillAlerts.AnyAsync(a => a.Id == foreign.Id));
        foreach (var row in await db.BillStatements.AsNoTracking().ToListAsync())
            Assert.Equal(facts[row.Id], new { row.TotalAmount, row.CurrencyCode, row.DueDate, row.PeriodStart, row.PeriodEnd });
        Assert.Equal(facts.Count, await db.BillStatements.CountAsync());
    }

    public static IEnumerable<object[]> Failures()
    {
        foreach (var operation in new[] { "create", "update", "remove" })
        foreach (var mode in new[] { "database", "cancel", "unexpected" })
        foreach (var callerChanges in new[] { false, true })
            yield return [operation, mode, callerChanges];
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task FailedMaintenance_DoesNotLeakIntoCallerSave_AndRetrySucceeds(
        string operation, string mode, bool callerChanges)
    {
        var failure = new Failure(mode);
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var bill = Bill(owner); var foreignBill = Bill(other);
        var primary = Alert(owner, bill.Id, BillAlertType.PaymentDue);
        var duplicate = Alert(owner, bill.Id, BillAlertType.PaymentDue);
        duplicate.CreatedAtUtc = Old.AddMinutes(1);
        var foreign = Alert(other, foreignBill.Id, BillAlertType.PaymentDue);
        var caller = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller original" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller deletion" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller insertion" };
        var statement = Statement(owner, bill.Id);
        if (operation == "remove") statement.TotalAmount = 0m;
        db.AddRange(bill, foreignBill, foreign, statement, caller, deleted);
        if (operation != "create") db.AddRange(primary, duplicate);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (callerChanges)
        {
            caller = await db.BankConnections.SingleAsync(c => c.Id == caller.Id);
            caller.InstitutionName = "Caller pending";
            db.Remove(await db.BankConnections.SingleAsync(c => c.Id == deleted.Id)); db.Add(added);
            if (operation != "create")
            {
                primary = await db.BillAlerts.SingleAsync(a => a.Id == primary.Id);
                primary.UpdatedAtUtc = Old.AddDays(1);
            }
        }
        var baseline = db.ChangeTracker.Entries().Select(e => new Tracked(e.Entity, e.State,
            e.CurrentValues.Clone(), e.OriginalValues.Clone(),
            e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray())).ToArray();
        using var cancellation = new CancellationTokenSource();
        failure.Cancel = cancellation.Cancel; failure.Armed = true;
        var maintenance = Maintenance(db);
        var error = await Record.ExceptionAsync(() => maintenance.ReconcileOneAsync(
            owner, bill.Id, Today, DateTimeOffset.UtcNow, cancellation.Token));
        Assert.NotNull(error);
        if (mode == "database") Assert.IsType<DbUpdateException>(error);
        else if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(1, failure.Failures);
        foreach (var row in baseline)
        {
            var entry = db.Entry(row.Entity);
            Assert.Equal(row.State, entry.State);
            Assert.Equal(row.Modified, entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order());
            foreach (var property in row.Current.Properties)
            {
                Assert.Equal(row.Current[property], entry.CurrentValues[property]);
                Assert.Equal(row.Original[property], entry.OriginalValues[property]);
            }
        }
        await db.SaveChangesAsync();
        await using (var stored = new FullWorthDbContext(options))
        {
            var alerts = await stored.BillAlerts.Where(a => a.UserId == owner).ToListAsync();
            Assert.Equal(operation == "create" ? 0 : 2, alerts.Count);
            Assert.All(alerts, alert =>
            {
                Assert.Equal("Original reminder", alert.Message);
                Assert.True(alert.IsRead); Assert.True(alert.IsDismissed);
                Assert.Equal(callerChanges && alert.Id == primary.Id ? Old.AddDays(1) : Old, alert.UpdatedAtUtc);
            });
            Assert.True(await stored.BillAlerts.AnyAsync(a => a.Id == foreign.Id));
            Assert.Equal(callerChanges ? "Caller pending" : "Caller original",
                (await stored.BankConnections.SingleAsync(c => c.Id == caller.Id)).InstitutionName);
            Assert.Equal(callerChanges, await stored.BankConnections.AnyAsync(c => c.Id == added.Id));
            Assert.Equal(!callerChanges, await stored.BankConnections.AnyAsync(c => c.Id == deleted.Id));
            Assert.Equal(operation == "remove" ? 0m : 25m, (await stored.BillStatements.SingleAsync()).TotalAmount);
        }
        await maintenance.ReconcileOneAsync(owner, bill.Id, Today, DateTimeOffset.UtcNow);
        await using var final = new FullWorthDbContext(options);
        var current = await final.BillAlerts.Where(a => a.UserId == owner).ToListAsync();
        if (operation == "remove") Assert.Empty(current);
        else
        {
            var alert = Assert.Single(current);
            Assert.Contains("$25.00", alert.Message);
            Assert.False(alert.IsRead); Assert.False(alert.IsDismissed);
        }
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("eur")]
    public async Task ValidLatestStatement_RetainsExistingCreationRulesAndIdentity(string currency)
    {
        await using var db = Db();
        var owner = Guid.NewGuid(); var bill = Bill(owner);
        var alert = Alert(owner, bill.Id, BillAlertType.PaymentDue);
        var statement = Statement(owner, bill.Id); statement.CurrencyCode = currency;
        db.AddRange(bill, alert, statement); await db.SaveChangesAsync();
        await Maintenance(db).ReconcileOneAsync(owner, bill.Id, Today, DateTimeOffset.UtcNow);
        var current = Assert.Single(await db.BillAlerts.ToListAsync());
        Assert.Equal(alert.Id, current.Id);
        Assert.Contains(currency == "USD" ? "$25.00" : "EUR 25.00", current.Message);
        Assert.Equal(BillAlertSeverity.Info, current.Severity);
        Assert.Equal(currency, (await db.BillStatements.SingleAsync()).CurrencyCode);
    }

    [Fact]
    public async Task ForeignOrMissingBill_CannotClearOrMutateAnyAlert()
    {
        await using var db = Db();
        var owner = Guid.NewGuid(); var bill = Bill(owner);
        var alert = Alert(owner, bill.Id, BillAlertType.PaymentDue);
        db.AddRange(bill, alert); await db.SaveChangesAsync();
        await Maintenance(db).ReconcileOneAsync(Guid.NewGuid(), bill.Id, Today, DateTimeOffset.UtcNow);
        await Maintenance(db).ReconcileOneAsync(owner, Guid.NewGuid(), Today, DateTimeOffset.UtcNow);
        var service = new BillStatementPaymentDueAlertService(new BillStreamReadGateway(db), new BillAlertReconciliationGateway(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ClearAsync(Guid.NewGuid(), bill.Id, DateTimeOffset.UtcNow));
        Assert.Equal("Original reminder", (await db.BillAlerts.SingleAsync()).Message);
        Assert.Equal(EntityState.Unchanged, db.Entry(alert).State);
    }

    private static FullWorthDbContext Db() => new(new DbContextOptionsBuilder<FullWorthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static BillStatementDueAlertMaintenance Maintenance(FullWorthDbContext db) => new(
        db, new BillDueReminderMaintenanceReadGateway(db),
        new BillStatementPaymentDueAlertService(new BillStreamReadGateway(db), new BillAlertReconciliationGateway(db)));
    private static BillStreamEntity Bill(Guid owner) => new() { UserId = owner, ProviderName = "Owned internet", IsActive = true };
    private static BillAlertEntity Alert(Guid owner, Guid bill, BillAlertType type) => new()
    {
        UserId = owner, BillStreamId = bill, AlertType = type, Title = "Original title", Message = "Original reminder",
        IsRead = true, IsDismissed = true, CreatedAtUtc = Old, UpdatedAtUtc = Old
    };
    private static BillStatementEntity Statement(Guid owner, Guid bill) => new()
    {
        UserId = owner, BillStreamId = bill, PeriodStart = new DateOnly(2026, 10, 1),
        PeriodEnd = new DateOnly(2026, 10, 31), DueDate = Today.AddDays(14), TotalAmount = 25m, CurrencyCode = "USD"
    };
    private sealed record Tracked(object Entity, EntityState State,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues Current,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues Original, string[] Modified);
    private sealed class Failure(string mode) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Failures { get; private set; }
        public Action? Cancel { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false; Failures++;
                if (mode == "cancel") { Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
                if (mode == "unexpected") throw new InvalidOperationException("Synthetic pre-commit failure.");
                throw new DbUpdateException("Synthetic pre-commit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

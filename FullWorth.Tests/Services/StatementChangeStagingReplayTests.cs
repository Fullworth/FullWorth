using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class StatementChangeStagingReplayTests
{
    [Theory]
    [InlineData("persisted")]
    [InlineData("pending-current")]
    [InlineData("pending-both")]
    [InlineData("modified-current")]
    [InlineData("removed-current")]
    [InlineData("pending-line")]
    [InlineData("modified-line")]
    [InlineData("removed-line")]
    public async Task ReconcileBeforeCommit_UsesStagedEvidence_AndDoesNotDuplicateChanges(string scenario)
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var stream = new BillStreamEntity { UserId = owner, ProviderName = "Fixture" };
        var other = new BillStreamEntity { UserId = foreign, ProviderName = "Foreign" };
        db.AddRange(stream, other);
        await db.SaveChangesAsync();
        var previous = Statement(owner, stream.Id, 1, 100m);
        var current = Statement(owner, stream.Id, 2, scenario == "modified-current" ? 100m : 125m);
        var foreignPrevious = Statement(foreign, other.Id, 1, 800m);
        var foreignCurrent = Statement(foreign, other.Id, 2, 900m);
        db.AddRange(foreignPrevious, foreignCurrent);
        if (scenario != "pending-both") db.Add(previous);
        if (scenario is not "pending-current" and not "pending-both") db.Add(current);
        BillLineItemEntity? line = null;
        if (scenario is "modified-line" or "removed-line")
        {
            line = new BillLineItemEntity { UserId = owner, BillStatementId = current.Id,
                Description = "Fixture fee", Amount = 25m, Category = scenario == "modified-line" ? "Service" : "Fee" };
            db.Add(line);
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = Service(db);
        if (scenario == "pending-both") db.Add(previous);
        if (scenario is "pending-current" or "pending-both") db.Add(current);
        if (scenario == "modified-current")
            (await db.BillStatements.SingleAsync(x => x.Id == current.Id)).TotalAmount = 125m;
        if (scenario == "removed-current")
            db.Remove(await db.BillStatements.SingleAsync(x => x.Id == current.Id));
        if (scenario == "pending-line")
            db.Add(new BillLineItemEntity { UserId = owner, BillStatementId = current.Id,
                Description = "Fixture fee", Amount = 25m, Category = "Fee" });
        if (scenario == "modified-line")
            (await db.BillLineItems.SingleAsync(x => x.Id == line!.Id)).Category = "Fee";
        if (scenario == "removed-line")
            db.Remove(await db.BillLineItems.SingleAsync(x => x.Id == line!.Id));
        var first = await service.ReconcileAsync(owner, stream.Id);
        var expected = scenario == "removed-current" ? 0 : 1;
        Assert.Equal(expected, first.CreatedCount);
        var tracked = db.ChangeTracker.Entries<BillChangeEntity>().Where(x => x.State != EntityState.Deleted).ToArray();
        Assert.Equal(expected, tracked.Length);
        if (expected == 1) tracked[0].Entity.IsAcknowledged = true;
        foreach (var alert in db.BillAlerts.Local) { alert.IsRead = true; alert.IsDismissed = true; }
        var before = EvidenceAttemptTestSupport.Capture(db);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var replay = await service.ReconcileAsync(owner, stream.Id);
            Assert.Equal(0, replay.CreatedCount);
            Assert.Equal(0, replay.UpdatedCount);
            Assert.Equal(0, replay.RemovedCount);
        }
        EvidenceAttemptTestSupport.AssertRestored(db, before);
        await db.SaveChangesAsync();
        var changes = await db.BillChanges.AsNoTracking().Where(x => x.UserId == owner).ToListAsync();
        Assert.Equal(expected, changes.Count);
        if (expected == 1)
        {
            Assert.True(changes[0].IsAcknowledged);
            Assert.Equal(25m, changes[0].AmountDifference);
            Assert.Equal(300m, changes[0].AnnualizedImpact);
            var alerts = await db.BillAlerts.AsNoTracking().Where(x => x.UserId == owner).ToListAsync();
            Assert.All(alerts, alert => { Assert.True(alert.IsRead); Assert.True(alert.IsDismissed); });
            Assert.Equal(scenario is "pending-line" or "modified-line" ? 1 : 0,
                alerts.Count(x => x.AlertType == BillAlertType.NewFee));
        }
        Assert.Empty(await db.BillChanges.Where(x => x.UserId == foreign).ToListAsync());
        Assert.Equal(900m, (await db.BillStatements.AsNoTracking().SingleAsync(x => x.Id == foreignCurrent.Id)).TotalAmount);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StagingGatewayFailure_RestoresCallerState_AndLeavesNoPartialChangeGraph(
        bool existingChange, bool canceled)
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var stream = new BillStreamEntity { UserId = owner, ProviderName = "Fixture" };
        db.Add(stream);
        db.AddRange(Statement(owner, stream.Id, 1, 100m), Statement(owner, stream.Id, 2, 125m));
        await db.SaveChangesAsync();
        if (existingChange) { await Service(db).ReconcileAsync(owner, stream.Id); await db.SaveChangesAsync(); }
        db.ChangeTracker.Clear();
        var bank = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller pending insert" };
        db.Add(bank);
        var before = EvidenceAttemptTestSupport.Capture(db);
        using var stop = new CancellationTokenSource();
        var gateway = new FailAfterStaging(new BillAlertReconciliationGateway(db), canceled ? stop.Cancel : null);
        var error = await Record.ExceptionAsync(() => Service(db, gateway).ReconcileAsync(owner, stream.Id,
            cancellationToken: stop.Token));
        if (canceled) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        EvidenceAttemptTestSupport.AssertRestored(db, before);
        await db.SaveChangesAsync();
        Assert.Equal(existingChange ? 1 : 0, await db.BillChanges.CountAsync());
        Assert.Equal(existingChange ? 1 : 0, await db.BillAlerts.CountAsync());
        await Service(db).ReconcileAsync(owner, stream.Id);
        await db.SaveChangesAsync();
        Assert.Single(await db.BillChanges.ToListAsync());
        Assert.Single(await db.BillAlerts.ToListAsync());
        Assert.Equal("Caller pending insert", (await db.BankConnections.SingleAsync()).InstitutionName);
    }

    private static FullWorthDbContext Db() => new(new DbContextOptionsBuilder<FullWorthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static BillStatementEntity Statement(Guid owner, Guid stream, int month, decimal amount) => new()
    {
        UserId = owner, BillStreamId = stream, PeriodStart = new(2026, month, 1),
        PeriodEnd = new DateOnly(2026, month, 1).AddMonths(1).AddDays(-1),
        TotalAmount = amount, CurrencyCode = "USD"
    };
    private static BillStatementChangeDetectionService Service(FullWorthDbContext db,
        IBillAlertReconciliationGateway? alerts = null) => new(db, new BillStreamReadGateway(db),
            new BillStatementEvidenceAlertService(), alerts ?? new BillAlertReconciliationGateway(db));
    private sealed class FailAfterStaging(IBillAlertReconciliationGateway inner, Action? cancel)
        : IBillAlertReconciliationGateway
    {
        public async Task StageReconciliationAsync(Guid userId, Guid billStreamId,
            IReadOnlyCollection<BillAlertReconciliationScope> scopes, IReadOnlyCollection<Guid> removeBillChangeIds,
            DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await inner.StageReconciliationAsync(userId, billStreamId, scopes, removeBillChangeIds, now, cancellationToken);
            cancel?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("Synthetic staging failure.");
        }
    }
}

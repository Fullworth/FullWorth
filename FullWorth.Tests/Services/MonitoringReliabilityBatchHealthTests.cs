using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed partial class MonitoringReliabilityBatchTests
{
    public static IEnumerable<object[]> HealthCompositionCases()
    {
        foreach (var mode in new[] { "new", "persisted", "added", "deleted", "persisted-added", "deleted-added" })
        foreach (var attention in new[] { "none", "one", "many" })
            yield return [mode, attention];
    }

    [Theory]
    [MemberData(nameof(HealthCompositionCases))]
    public async Task HealthReconciliation_UsesLivePendingSlots_AndIsRepeatable(string mode, string attention)
    {
        var f = await Fixture.CreateAsync();
        var old = Health(f.Owner);
        var foreign = Health(f.Foreign, "Foreign retained health");
        await using (var seed = f.Open())
        {
            seed.Add(foreign);
            if (mode is not "new" and not "added") seed.Add(old);
            await seed.SaveChangesAsync();
        }
        await using var db = f.Open();
        if (mode is not "new" and not "added")
        {
            old = await db.BillAlerts.SingleAsync(x => x.Id == old.Id);
            if (mode.StartsWith("deleted", StringComparison.Ordinal)) db.Remove(old);
        }
        if (mode.Contains("added", StringComparison.Ordinal) || mode == "added")
        {
            var pending = Health(f.Owner);
            pending.CreatedAtUtc = Now.AddSeconds(1);
            db.Add(pending);
        }
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Pending caller bill" };
        db.Add(caller);
        IReadOnlyList<string> names = attention switch
        {
            "none" => [], "one" => ["Fixture Bank"], _ => ["Fixture Bank", "Second bank"]
        };
        var service = new BankConnectionHealthAlertService(db, new Banks(f.Owner, names));
        await service.ReconcileAsync(f.Owner);
        await service.ReconcileAsync(f.Owner);
        if (mode == "added" && attention is "one" or "none")
        {
            // Reusing/canceling an unchanged Added slot must not save unrelated work.
            Assert.Equal(EntityState.Added, db.Entry(caller).State);
            await using var before = f.Open();
            Assert.False(await before.BillStreams.AnyAsync(x => x.Id == caller.Id));
        }
        await db.SaveChangesAsync();
        await using var verify = f.Open();
        var alerts = await verify.BillAlerts.AsNoTracking()
            .Where(x => x.UserId == f.Owner && x.AlertType == BillAlertType.ConnectionIssue).ToListAsync();
        if (attention == "none") Assert.Empty(alerts);
        else
        {
            var alert = Assert.Single(alerts);
            Assert.Equal(HealthTitle, alert.Title);
            Assert.Equal(BillAlertSeverity.Warning, alert.Severity);
            if (attention == "one") Assert.Equal(HealthMessage, alert.Message);
            else Assert.Contains("2 bank connections", alert.Message);
            if (mode != "new" && mode != "deleted" && attention == "one")
            {
                Assert.True(alert.IsRead);
                Assert.True(alert.IsDismissed);
            }
        }
        var retained = await verify.BillAlerts.AsNoTracking().SingleAsync(x => x.Id == foreign.Id);
        Assert.Equal("Foreign retained health", retained.Message);
        Assert.True(retained.IsRead);
        Assert.True(retained.IsDismissed);
        await f.AssertHistoryAsync(verify);
    }

    public static IEnumerable<object[]> HealthFailureCases()
    {
        foreach (var mode in new[] { "replace-deleted", "update-added", "prune-added", "remove-mixed" })
        foreach (var kind in new[] { "database", "unexpected", "cancel" })
            yield return [mode, kind];
    }

    [Theory]
    [MemberData(nameof(HealthFailureCases))]
    public async Task FailedHealthComposition_RestoresPendingIntent_AndRetries(string mode, string kind)
    {
        var f = await Fixture.CreateAsync();
        var existing = Health(f.Owner);
        await using (var seed = f.Open()) { seed.Add(existing); await seed.SaveChangesAsync(); }
        var fault = new WriteFault { Armed = false, Error = Error(kind), Matches = _ => true };
        await using var db = f.Open(fault);
        existing = await db.BillAlerts.SingleAsync(x => x.Id == existing.Id);
        if (mode == "replace-deleted") db.Remove(existing);
        if (mode is "update-added" or "prune-added" or "remove-mixed")
        {
            if (mode == "update-added") db.Remove(existing);
            var pending = Health(f.Owner, mode == "update-added" ? "Old caller message" : HealthMessage);
            pending.CreatedAtUtc = Now.AddSeconds(1);
            db.Add(pending);
        }
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Preserve caller work" };
        db.Add(caller);
        var before = Capture(db);
        using var stop = new CancellationTokenSource();
        if (kind == "cancel") fault.Cancel = stop.Cancel;
        fault.Armed = true;
        IReadOnlyList<string> names = mode == "remove-mixed" ? [] : ["Fixture Bank"];
        var service = new BankConnectionHealthAlertService(db, new Banks(f.Owner, names));
        var error = await Record.ExceptionAsync(() => service.ReconcileAsync(f.Owner, stop.Token));
        if (kind == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.Same(fault.Error, error);
        Assert.Equal(1, fault.Failures);
        AssertRetained(db, before);
        Assert.Equal(before.Count, db.ChangeTracker.Entries().Count());
        await using (var durable = f.Open())
        {
            Assert.Equal(HealthMessage, (await durable.BillAlerts.AsNoTracking()
                .SingleAsync(x => x.Id == existing.Id)).Message);
            Assert.False(await durable.BillStreams.AnyAsync(x => x.Id == caller.Id));
        }
        await service.ReconcileAsync(f.Owner);
        await db.SaveChangesAsync();
        await using var verify = f.Open();
        var alerts = await verify.BillAlerts.AsNoTracking()
            .Where(x => x.UserId == f.Owner && x.AlertType == BillAlertType.ConnectionIssue).ToListAsync();
        if (mode == "remove-mixed") Assert.Empty(alerts);
        else Assert.Equal(HealthMessage, Assert.Single(alerts).Message);
        Assert.True(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        await f.AssertHistoryAsync(verify);
    }

    [Fact]
    public async Task HealthCancellation_StopsBeforeAnyTrackingOrSave()
    {
        var f = await Fixture.CreateAsync();
        await using var db = f.Open();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BankConnectionHealthAlertService(db, new Banks(f.Owner, ["Fixture Bank"]))
                .ReconcileAsync(f.Owner, stop.Token));
        Assert.Empty(db.ChangeTracker.Entries());
    }
}

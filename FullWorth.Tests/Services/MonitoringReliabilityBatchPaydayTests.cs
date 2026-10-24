using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FullWorth.Tests.Services;

public sealed partial class MonitoringReliabilityBatchTests
{
    public static IEnumerable<object[]> DurablePaydayCases()
    {
        foreach (var change in new[] { "title", "message", "severity", "deleted" })
        foreach (var tracked in new[] { false, true })
        foreach (var acknowledged in new[] { false, true })
            yield return [change, tracked, acknowledged];
    }

    [Theory]
    [MemberData(nameof(DurablePaydayCases))]
    public async Task PaydayReplay_UsesDurableImmutableContent_NotIdentityMap(
        string change, bool tracked, bool acknowledged)
    {
        var f = await Fixture.CreateAsync();
        var request = Request(Guid.NewGuid());
        var original = Payday(f.Owner, request);
        var foreign = Payday(f.Foreign, request);
        foreign.Message = "Foreign retained guidance";
        await using (var seed = f.Open()) { seed.AddRange(original, foreign); await seed.SaveChangesAsync(); }
        await using var db = f.Open();
        BillAlertEntity? local = tracked
            ? await db.BillAlerts.SingleAsync(x => x.Id == original.Id) : null;
        if (local is not null && acknowledged) local.IsRead = local.IsDismissed = true;
        await using (var other = f.Open())
        {
            var winner = await other.BillAlerts.SingleAsync(x => x.Id == original.Id);
            switch (change)
            {
                case "title": winner.Title = "Different durable title"; break;
                case "message": winner.Message = "Different durable message"; break;
                case "severity": winner.Severity = BillAlertSeverity.Critical; break;
                case "deleted": other.Remove(winner); break;
            }
            await other.SaveChangesAsync();
        }
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Pending unrelated caller" };
        db.Add(caller);
        var before = Capture(db);
        var gateway = new PlanningPaydayAlertGateway(db);
        if (change == "deleted" && local is not null && acknowledged)
        {
            // A caller acknowledgement targets a row removed in another
            // context. Preserve that intent and reject it before writing,
            // instead of confirming a replacement or discarding the edit.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.EnsureAsync(f.Owner, request, Now.AddHours(1)));
            AssertRetained(db, before);
            await using var verify = f.Open();
            Assert.False(await verify.BillAlerts.AnyAsync(x =>
                x.UserId == f.Owner && x.SourceEventId == request.SourceEventId));
            Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        }
        else if (change == "deleted")
        {
            await gateway.EnsureAsync(f.Owner, request, Now.AddHours(1));
            await using var verify = f.Open();
            var replacement = await verify.BillAlerts.AsNoTracking()
                .SingleAsync(x => x.UserId == f.Owner && x.SourceEventId == request.SourceEventId);
            Assert.NotEqual(original.Id, replacement.Id);
            Assert.Equal(request.Message, replacement.Message);
            Assert.True(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.EnsureAsync(f.Owner, request, Now.AddHours(1)));
            AssertRetained(db, before);
            await using var verify = f.Open();
            Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        }
        await using var retained = f.Open();
        Assert.Equal("Foreign retained guidance",
            (await retained.BillAlerts.AsNoTracking().SingleAsync(x => x.Id == foreign.Id)).Message);
        await f.AssertHistoryAsync(retained);
    }

    public static IEnumerable<object[]> PendingPaydayCases()
    {
        foreach (var change in new[] { "title", "message", "severity", "source", "type",
                     "stream", "change", "deleted", "added" })
        foreach (var acknowledged in new[] { false, true })
            yield return [change, acknowledged];
    }

    [Theory]
    [MemberData(nameof(PendingPaydayCases))]
    public async Task PaydayPendingImmutableIntent_FailsClosedWithoutSavingOrDiscarding(
        string change, bool acknowledged)
    {
        var f = await Fixture.CreateAsync();
        var request = Request(Guid.NewGuid());
        var alert = Payday(f.Owner, request);
        await using (var seed = f.Open())
        {
            if (change != "added") seed.Add(alert);
            await seed.SaveChangesAsync();
        }
        await using var db = f.Open();
        if (change == "added") db.Add(alert);
        else alert = await db.BillAlerts.SingleAsync(x => x.Id == alert.Id);
        switch (change)
        {
            case "title": alert.Title = "Pending title"; break;
            case "message": alert.Message = "Pending guidance"; break;
            case "severity": alert.Severity = BillAlertSeverity.Critical; break;
            case "source": alert.SourceEventId = Guid.NewGuid(); break;
            case "type": alert.AlertType = BillAlertType.ConnectionIssue; break;
            case "stream": alert.BillStreamId = f.Bill; break;
            case "change": alert.BillChangeId = Guid.NewGuid(); break;
            case "deleted": db.Remove(alert); break;
        }
        if (acknowledged) alert.IsRead = alert.IsDismissed = true;
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Caller bill" };
        db.Add(caller);
        var before = Capture(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PlanningPaydayAlertGateway(db).EnsureAsync(f.Owner, request, Now));
        AssertRetained(db, before);
        Assert.Equal(before.Count, db.ChangeTracker.Entries().Count());
        await using var verify = f.Open();
        Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        var stored = await verify.BillAlerts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == alert.Id);
        if (change == "added") Assert.Null(stored);
        else Assert.Equal(request.Message, stored!.Message);
        await f.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PaydayReplay_PreservesAcknowledgements_AndDoesNotSaveUnrelatedWork(bool read, bool dismissed)
    {
        var f = await Fixture.CreateAsync();
        var request = Request(Guid.NewGuid());
        var alert = Payday(f.Owner, request);
        await using (var seed = f.Open()) { seed.Add(alert); await seed.SaveChangesAsync(); }
        await using var db = f.Open();
        alert = await db.BillAlerts.SingleAsync(x => x.Id == alert.Id);
        alert.IsRead = read;
        alert.IsDismissed = dismissed;
        alert.UpdatedAtUtc = Now.AddMinutes(1);
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Keep pending" };
        db.Add(caller);
        var before = Capture(db);
        await new PlanningPaydayAlertGateway(db).EnsureAsync(f.Owner, request, Now.AddHours(1));
        AssertRetained(db, before);
        await using (var verify = f.Open())
            Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
        await db.SaveChangesAsync();
        await using var stored = f.Open();
        var result = await stored.BillAlerts.AsNoTracking().SingleAsync(x => x.Id == alert.Id);
        Assert.Equal(read, result.IsRead);
        Assert.Equal(dismissed, result.IsDismissed);
        Assert.Equal(Now.AddMinutes(1), result.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("identical", false)]
    [InlineData("identical", true)]
    [InlineData("conflict", false)]
    [InlineData("conflict", true)]
    [InlineData("unrelated", false)]
    [InlineData("unrelated", true)]
    public async Task PaydayConcurrentInsert_PreservesStrictDurableRaceSemantics(string mode, bool callerChanges)
    {
        var f = await Fixture.CreateAsync();
        var request = Request(Guid.NewGuid());
        var fault = new WriteFault
        {
            Armed = true, Error = Error("database"), UseRealUniqueConstraint = mode != "unrelated",
            Matches = db => db.ChangeTracker.Entries<BillAlertEntity>()
                .Any(x => x.State == EntityState.Added && x.Entity.UserId == f.Owner)
        };
        if (mode != "unrelated")
            fault.BeforeFailure = async () =>
            {
                await using var winnerDb = f.Open();
                var winner = Payday(f.Owner, request);
                winner.IsRead = winner.IsDismissed = true;
                if (mode == "conflict") winner.Message = "Different immutable winner";
                winnerDb.Add(winner);
                await winnerDb.SaveChangesAsync();
            };
        await using var db = f.Open(fault);
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Concurrent caller" };
        if (callerChanges) db.Add(caller);
        var error = await Record.ExceptionAsync(() =>
            new PlanningPaydayAlertGateway(db).EnsureAsync(f.Owner, request, Now));
        if (mode == "identical") Assert.Null(error);
        else if (mode == "conflict") Assert.IsType<InvalidOperationException>(error);
        else Assert.Same(fault.Error, error);
        Assert.Equal(1, fault.Failures);
        Assert.DoesNotContain(db.ChangeTracker.Entries<BillAlertEntity>(), x => x.State == EntityState.Added);
        if (callerChanges) Assert.Equal(EntityState.Added, db.Entry(caller).State);
        await db.SaveChangesAsync();
        await using var verify = f.Open();
        var saved = await verify.BillAlerts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.UserId == f.Owner && x.SourceEventId == request.SourceEventId);
        if (mode == "unrelated") Assert.Null(saved);
        else
        {
            Assert.NotNull(saved);
            Assert.Equal(mode == "conflict" ? "Different immutable winner" : request.Message, saved.Message);
            Assert.True(saved.IsRead);
            Assert.True(saved.IsDismissed);
        }
        await f.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignPendingAlert_DoesNotBlockOwnedIdempotency(bool changeIdentity)
    {
        var f = await Fixture.CreateAsync();
        var request = Request(Guid.NewGuid());
        var foreign = Payday(f.Foreign, request);
        await using (var seed = f.Open()) { seed.Add(foreign); await seed.SaveChangesAsync(); }
        await using var db = f.Open();
        foreign = await db.BillAlerts.SingleAsync(x => x.Id == foreign.Id);
        if (changeIdentity) foreign.SourceEventId = Guid.NewGuid();
        else foreign.Message = "Foreign pending caller edit";
        // Seed the owned durable result separately so Ensure is a read/no-op.
        await using (var other = f.Open()) { other.Add(Payday(f.Owner, request)); await other.SaveChangesAsync(); }
        var before = Capture(db);
        await new PlanningPaydayAlertGateway(db).EnsureAsync(f.Owner, request, Now);
        AssertRetained(db, before);
        await using var verify = f.Open();
        Assert.Equal(request.Message,
            (await verify.BillAlerts.AsNoTracking().SingleAsync(x => x.Id == foreign.Id)).Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PaydayCanceledFastPath_DoesNotAcknowledgeOrStage(bool existing)
    {
        var f = await Fixture.CreateAsync();
        var request = Request(Guid.NewGuid());
        await using var db = f.Open();
        if (existing)
        {
            db.Add(Payday(f.Owner, request));
            await db.SaveChangesAsync();
        }
        var before = Capture(db);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PlanningPaydayAlertGateway(db).EnsureAsync(f.Owner, request, Now, stop.Token));
        AssertRetained(db, before);
    }
}

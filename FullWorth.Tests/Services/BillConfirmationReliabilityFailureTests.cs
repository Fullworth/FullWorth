using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed partial class BillConfirmationReliabilityTests
{
    public static IEnumerable<object[]> ConfirmationFailureCases()
    {
        foreach (var operation in new[] { "confirm", "existing", "new" })
        foreach (var kind in new[] { "conflict", "database", "unexpected", "cancel" })
        foreach (var callerTimestamp in new[] { false, true })
        foreach (var active in operation == "new" ? new[] { true } : new[] { false, true })
            yield return [operation, kind, callerTimestamp, active];
    }

    [Theory]
    [MemberData(nameof(ConfirmationFailureCases))]
    public async Task FailedConfirmation_RestoresOnlyItsAttempt_AndPreservesCallerWork(
        string operation, string kind, bool callerTimestamp, bool active)
    {
        var f = await Fixture.CreateAsync();
        await using (var setup = f.Open())
        {
            var bill = await setup.BillStreams.SingleAsync(x => x.Id == f.Bill);
            bill.IsActive = active;
            bill.UpdatedAtUtc = Now;
            if (operation == "new") setup.Remove(bill);
            await setup.SaveChangesAsync();
        }
        var failure = kind switch
        {
            "conflict" => (Exception)new DbUpdateConcurrencyException("Synthetic concurrency failure."),
            "database" => new DbUpdateException("Synthetic pre-save failure."),
            _ => new IOException("Synthetic operation failure.")
        };
        var fault = new WriteFault { Matches = _ => true, Error = failure };
        await using var db = f.Open(fault);
        BillStreamEntity? target = operation == "new" ? null :
            await db.BillStreams.SingleAsync(x => x.Id == f.Bill);
        if (target is not null && callerTimestamp) target.UpdatedAtUtc = Now.AddMinutes(1);
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Pending caller insert" };
        db.Add(caller);
        var foreign = await db.BillStreams.SingleAsync(x => x.Id == f.ForeignBill);
        foreign.ProviderName = "Pending foreign caller edit";
        if (target is null && callerTimestamp) foreign.UpdatedAtUtc = Now.AddMinutes(1);
        var before = Capture(db);
        using var stop = new CancellationTokenSource();
        if (kind == "cancel") fault.Cancel = stop.Cancel;
        fault.Armed = true;
        using var host = new Host(db, f.Owner);
        var result = default(IActionResult);
        var error = await Record.ExceptionAsync(async () => result = await Act(host, operation, f.Bill, stop.Token));
        if (kind == "conflict" && operation != "new") Assert.IsType<ConflictObjectResult>(result);
        else if (kind == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.Same(failure, error);
        Assert.Equal(1, fault.Failures);
        AssertRetained(db, before);
        Assert.Equal(before.Count, db.ChangeTracker.Entries().Count());
        await using (var verify = f.Open())
        {
            Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
            var durable = await verify.BillStreams.AsNoTracking().SingleOrDefaultAsync(x => x.Id == f.Bill);
            if (operation == "new") Assert.Null(durable);
            else
            {
                Assert.Equal(BillStreamSource.AutomaticDiscovery, durable!.Source);
                Assert.Equal(active, durable.IsActive);
                Assert.Equal(BillCategory.Unknown, durable.Category);
            }
        }
        await db.SaveChangesAsync();
        await using (var verify = f.Open())
        {
            Assert.Equal(operation == "new" ? 0 : 1,
                await verify.BillStreams.CountAsync(x => x.UserId == f.Owner && x.ProviderName == "Target"));
            Assert.True(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
            await f.AssertHistoryAsync(verify);
        }
        var retry = await Act(host, operation, f.Bill);
        if (operation == "confirm") Assert.IsType<NoContentResult>(retry);
        else Assert.Equal("Manual", Created(retry).Source);
        await using var stored = f.Open();
        var confirmed = await stored.BillStreams.AsNoTracking()
            .SingleAsync(x => x.UserId == f.Owner && x.ProviderName == "Target");
        Assert.Equal(BillStreamSource.Manual, confirmed.Source);
        Assert.True(confirmed.IsActive);
        Assert.Equal(operation == "confirm" ? BillCategory.Unknown : BillCategory.Internet, confirmed.Category);
        Assert.Equal("Pending foreign caller edit",
            (await stored.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.ForeignBill)).ProviderName);
        await f.AssertHistoryAsync(stored);
    }

    public static IEnumerable<object[]> ConfirmationRaceCases()
    {
        foreach (var operation in new[] { "confirm", "existing" })
        foreach (var originalActive in new[] { false, true })
        foreach (var winnerActive in new[] { false, true })
            yield return [operation, originalActive, winnerActive];
    }

    [Theory]
    [MemberData(nameof(ConfirmationRaceCases))]
    public async Task RealConcurrencyPredicate_PreservesWinner_AndUnrelatedTracking(
        string operation, bool originalActive, bool winnerActive)
    {
        var f = await Fixture.CreateAsync();
        await using (var setup = f.Open())
        {
            (await setup.BillStreams.SingleAsync(x => x.Id == f.Bill)).IsActive = originalActive;
            await setup.SaveChangesAsync();
        }
        var fault = new WriteFault
        {
            Armed = true, Matches = _ => true, LetSaveProceed = true,
            Error = new DbUpdateConcurrencyException("Synthetic InMemory counterpart of the SQL predicate conflict."),
            BeforeFailure = async () =>
            {
                await using var winner = f.Open();
                var bill = await winner.BillStreams.SingleAsync(x => x.Id == f.Bill);
                bill.Source = BillStreamSource.Manual;
                bill.IsActive = winnerActive;
                await winner.SaveChangesAsync();
            }
        };
        await using var db = f.Open(fault);
        var target = await db.BillStreams.SingleAsync(x => x.Id == f.Bill);
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Concurrent caller" };
        db.Add(caller);
        var before = Capture(db);
        using var host = new Host(db, f.Owner);
        Assert.IsType<ConflictObjectResult>(await Act(host, operation, f.Bill));
        AssertRetained(db, before);
        await using (var verify = f.Open())
        {
            Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id));
            var winner = await verify.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.Bill);
            Assert.Equal(BillStreamSource.Manual, winner.Source);
            Assert.Equal(winnerActive, winner.IsActive);
        }
        // A later caller save does not overwrite the unchanged stale target.
        await db.SaveChangesAsync();
        await using var stored = f.Open();
        Assert.True(await stored.BillStreams.AnyAsync(x => x.Id == caller.Id));
        Assert.Equal(winnerActive, (await stored.BillStreams.AsNoTracking().SingleAsync(x => x.Id == f.Bill)).IsActive);
        await f.AssertHistoryAsync(stored);
    }
}

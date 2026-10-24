using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed partial class MonitoringReliabilityBatchTests
{
    public static IEnumerable<object[]> RefreshFailureCases()
    {
        foreach (var stage in new[] { "facts", "run", "alert" })
        foreach (var index in new[] { 0, 1, 2 })
        foreach (var kind in new[] { "database", "unexpected", "provider-cancel" })
            yield return [stage, index, kind];
    }

    [Theory]
    [MemberData(nameof(RefreshFailureCases))]
    public async Task PaydayBatch_AttemptsHealthySiblings_PreservesFirstFailure_AndRetries(
        string stage, int index, string kind)
    {
        var f = await Fixture.CreateAsync();
        var payrolls = new Payrolls(f.Owner);
        var target = payrolls.Facts[index].TransactionId;
        var failure = Error(kind);
        var fault = new WriteFault { Error = failure };
        var failFacts = stage == "facts";
        payrolls.BeforeIndividual = fact =>
        {
            if (failFacts && fact.TransactionId == target)
            {
                failFacts = false;
                throw failure;
            }
        };
        fault.Matches = db => stage == "run"
            ? db.ChangeTracker.Entries<PlanningPaycheckPlanRunEntity>()
                .Any(x => x.State == EntityState.Added && x.Entity.PayrollTransactionId == target)
            : stage == "alert" && db.ChangeTracker.Entries<BillAlertEntity>()
                .Any(x => x.State == EntityState.Added && x.Entity.SourceEventId == target);
        await using var db = f.Open(fault);
        var caller = new BillStreamEntity { UserId = f.Owner, ProviderName = "Retained batch caller" };
        db.Add(caller);
        fault.Armed = stage != "facts";
        var service = Refresh(f, db, payrolls);
        var error = await Record.ExceptionAsync(() => service.RefreshAsync(f.Owner));
        Assert.Same(failure, error);
        Assert.Equal(payrolls.Facts.Select(x => x.TransactionId), payrolls.Attempts);
        Assert.Contains(stage == "facts"
            ? nameof(PaydayBatch_AttemptsHealthySiblings_PreservesFirstFailure_AndRetries)
            : nameof(WriteFault.SavingChangesAsync), error!.StackTrace!);
        Assert.DoesNotContain(db.ChangeTracker.Entries<BillAlertEntity>(),
            x => x.State == EntityState.Added && x.Entity.SourceEventId == target);
        Assert.DoesNotContain(db.ChangeTracker.Entries<PlanningPaycheckPlanRunEntity>(),
            x => x.State == EntityState.Added);
        // A later unrelated save must not leak the failed alert/run.
        await db.SaveChangesAsync();
        await using (var before = f.Open())
        {
            var alerts = await before.BillAlerts.AsNoTracking()
                .Where(x => x.UserId == f.Owner && x.AlertType == BillAlertType.PaydayPlan).ToListAsync();
            Assert.Equal(2, alerts.Count);
            Assert.DoesNotContain(alerts, x => x.SourceEventId == target);
            Assert.Equal(stage == "alert" ? 3 : 2,
                await before.PlanningPaycheckPlanRuns.CountAsync(x => x.UserId == f.Owner));
            Assert.True(await before.BillStreams.AnyAsync(x => x.Id == caller.Id));
            await f.AssertHistoryAsync(before);
        }
        payrolls.Attempts.Clear();
        var result = await service.RefreshAsync(f.Owner);
        Assert.Equal(3, result.PayrollFactsScanned);
        Assert.Equal(3, result.ReadyPlansEnsured);
        await using var verify = f.Open();
        Assert.Equal(3, await verify.BillAlerts.CountAsync(x =>
            x.UserId == f.Owner && x.AlertType == BillAlertType.PaydayPlan));
        Assert.Equal(3, await verify.PlanningPaycheckPlanRuns.CountAsync(x => x.UserId == f.Owner));
        Assert.Equal(0, await verify.PlanningPaycheckAllocations.CountAsync(x => x.UserId == f.Owner));
        Assert.All(await verify.PlanningPaycheckPlanRuns.AsNoTracking()
            .Where(x => x.UserId == f.Owner).ToListAsync(), x =>
        {
            Assert.Equal(1000m, x.PaycheckAmount);
            Assert.Equal(0m, x.RecommendedSetAside);
            Assert.Equal(1000m, x.PaycheckRemainingAfterPlan);
            Assert.Equal(0m, x.Shortfall);
        });
        await f.AssertHistoryAsync(verify);
    }

    public static IEnumerable<object[]> RefreshCancellationCases()
    {
        foreach (var stage in new[] { "facts", "run", "alert" })
        foreach (var index in new[] { 0, 1, 2 })
            yield return [stage, index];
    }

    [Theory]
    [MemberData(nameof(RefreshCancellationCases))]
    public async Task RequestCancellation_StopsBeforeLaterPaychecks(string stage, int index)
    {
        var f = await Fixture.CreateAsync();
        var payrolls = new Payrolls(f.Owner);
        var target = payrolls.Facts[index].TransactionId;
        using var stop = new CancellationTokenSource();
        var fault = new WriteFault
        {
            Armed = stage != "facts", Cancel = stop.Cancel,
            Matches = db => stage == "run"
                ? db.ChangeTracker.Entries<PlanningPaycheckPlanRunEntity>()
                    .Any(x => x.State == EntityState.Added && x.Entity.PayrollTransactionId == target)
                : db.ChangeTracker.Entries<BillAlertEntity>()
                    .Any(x => x.State == EntityState.Added && x.Entity.SourceEventId == target)
        };
        payrolls.BeforeIndividual = fact =>
        {
            if (stage == "facts" && fact.TransactionId == target)
            {
                stop.Cancel();
                stop.Token.ThrowIfCancellationRequested();
            }
        };
        await using var db = f.Open(fault);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Refresh(f, db, payrolls).RefreshAsync(f.Owner, stop.Token));
        Assert.Equal(payrolls.Facts.Take(index + 1).Select(x => x.TransactionId), payrolls.Attempts);
        await db.SaveChangesAsync();
        await using var verify = f.Open();
        Assert.Equal(index, await verify.BillAlerts.CountAsync(x =>
            x.UserId == f.Owner && x.AlertType == BillAlertType.PaydayPlan));
        Assert.Equal(index + (stage == "alert" ? 1 : 0),
            await verify.PlanningPaycheckPlanRuns.CountAsync(x => x.UserId == f.Owner));
        await f.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData("database")]
    [InlineData("unexpected")]
    [InlineData("provider-cancel")]
    public async Task MultiplePaycheckFailures_StillAttemptMiddle_AndRethrowOriginalFirst(string kind)
    {
        var f = await Fixture.CreateAsync();
        var payrolls = new Payrolls(f.Owner);
        var first = Error(kind);
        var last = new InvalidOperationException("Later independent failure.");
        payrolls.BeforeIndividual = fact =>
        {
            if (fact.TransactionId == payrolls.Facts[0].TransactionId) throw first;
            if (fact.TransactionId == payrolls.Facts[2].TransactionId) throw last;
        };
        await using var db = f.Open();
        var error = await Record.ExceptionAsync(() => Refresh(f, db, payrolls).RefreshAsync(f.Owner));
        Assert.Same(first, error);
        Assert.Equal(3, payrolls.Attempts.Count);
        await using var verify = f.Open();
        var saved = Assert.Single(await verify.BillAlerts.AsNoTracking()
            .Where(x => x.UserId == f.Owner && x.AlertType == BillAlertType.PaydayPlan).ToListAsync());
        Assert.Equal(payrolls.Facts[1].TransactionId, saved.SourceEventId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingSchedule_StopsGlobalWork_WithoutMaskingEarlierFailure(bool earlierFailure)
    {
        var f = await Fixture.CreateAsync();
        var payrolls = new Payrolls(f.Owner);
        var failure = Error("unexpected");
        await using (var setup = f.Open())
        {
            setup.RemoveRange(await setup.PlanningPaySchedules.Where(x => x.UserId == f.Owner).ToListAsync());
            await setup.SaveChangesAsync();
        }
        if (earlierFailure) payrolls.BeforeIndividual = fact =>
        {
            if (fact.TransactionId == payrolls.Facts[0].TransactionId) throw failure;
        };
        await using var db = f.Open();
        var service = Refresh(f, db, payrolls);
        if (earlierFailure)
        {
            var error = await Record.ExceptionAsync(() => service.RefreshAsync(f.Owner));
            Assert.Same(failure, error);
            Assert.Equal(2, payrolls.Attempts.Count);
        }
        else
        {
            var result = await service.RefreshAsync(f.Owner);
            Assert.True(result.PayScheduleRequired);
            Assert.Equal(1, result.PayrollFactsScanned);
            Assert.Single(payrolls.Attempts);
        }
        Assert.Empty(await db.PlanningPaycheckPlanRuns.Where(x => x.UserId == f.Owner).ToListAsync());
    }

    [Theory]
    [InlineData("unsupported")]
    [InlineData("duplicate")]
    [InlineData("empty")]
    public async Task OrdinaryBatch_RetainsSkipDeduplicationAndReplaySemantics(string mode)
    {
        var f = await Fixture.CreateAsync();
        var payrolls = new Payrolls(f.Owner);
        if (mode == "unsupported") payrolls.Facts[0] = payrolls.Facts[0] with { CurrencyCode = null };
        if (mode == "duplicate") payrolls.Facts.Add(payrolls.Facts[0]);
        if (mode == "empty") payrolls.Facts.Clear();
        // The production source contract deduplicates opaque IDs for the batch.
        // Per-day facts are unique; remove a repeated list row at individual lookup.
        var adapter = new DeduplicatedPayrolls(payrolls);
        await using var db = f.Open();
        var streams = new EmptyBills();
        var plan = new FullWorth.API.Services.Planning.PlanningPaydayPlanService(
            new(db, streams, new Clock()), new(db, new Clock()), streams, new UnusedFacts(), adapter);
        var service = new FullWorth.API.Services.Planning.PlanningPaydayAlertRefreshService(
            plan, adapter, new PlanningPaydayAlertGateway(db), new Clock());
        var result = await service.RefreshAsync(f.Owner);
        Assert.Equal(mode == "empty" ? 0 : 3, result.PayrollFactsScanned);
        Assert.Equal(mode == "empty" ? 0 : mode == "unsupported" ? 2 : 3, result.ReadyPlansEnsured);
        Assert.Equal(mode == "unsupported" ? 1 : 0, result.SkippedFacts);
        Assert.False(result.PayScheduleRequired);
        var replay = await service.RefreshAsync(f.Owner);
        Assert.Equal(result, replay);
    }

    [Fact]
    public async Task CanceledEmptyBatch_IsNotReportedAsSuccessful()
    {
        var f = await Fixture.CreateAsync();
        var payrolls = new Payrolls(f.Owner);
        payrolls.Facts.Clear();
        await using var db = f.Open();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Refresh(f, db, payrolls).RefreshAsync(f.Owner, stop.Token));
        Assert.Equal(0, payrolls.Reads);
    }

    private sealed class DeduplicatedPayrolls(Payrolls inner) : IPlanningPostedPayrollFactsGateway
    {
        public async Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(Guid user,
            DateOnly from, DateOnly through, CancellationToken token = default)
        {
            var facts = await inner.GetAsync(user, from, through, token);
            return from == through ? facts.DistinctBy(x => x.TransactionId).ToArray() : facts;
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace FullWorth.Tests.Services;

public sealed class PaycheckPlanPersistenceFailureTests
{
    private static readonly DateOnly Posted = new(2026, 9, 25);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-26T00:00:00Z");

    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var complete in new[] { false, true })
        foreach (var mode in new[] { "save", "cancel", "unexpected", "race-identical", "race-conflict" })
        foreach (var callerChanges in new[] { false, true })
            yield return [complete, mode, callerChanges, false];

        foreach (var mode in new[] { "save", "cancel" })
        foreach (var callerChanges in new[] { false, true })
            yield return [true, mode, callerChanges, true];
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task FailedPlanSave_DiscardsOnlyItsCandidates_PreservesCallerWork_AndRetainsImmutableReplay(
        bool complete, string mode, bool callerChanges, bool zeroAllocations)
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var plain = new DbContextOptionsBuilder<FullWorthDbContext>().UseInMemoryDatabase(name, root).Options;
        var owner = Guid.NewGuid();
        var payroll = Guid.NewGuid();
        PlanningPaycheckAllocationDraft[] allocations = zeroAllocations ? [] :
        [
            new(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 30),
                new DateOnly(2026, 10, 15), 40m, "USD"),
            new(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 9, 30),
                new DateOnly(2026, 10, 16), 60m, "USD")
        ];
        var run = new PlanningPaycheckPlanRunDraft(Posted, 250m, "USD",
            zeroAllocations ? 0m : 100m, zeroAllocations ? 250m : 150m, 0m)
        {
            Coverage = new PlanningPaycheckPlanCoverage(zeroAllocations ? 0 : 2, 1, 0, 0, 0, 0)
        };
        var failure = new PlanFailure
        {
            Mode = mode, PlainOptions = plain, Complete = complete, Owner = owner,
            Payroll = payroll, Run = run, Allocations = allocations
        };
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(name, root).AddInterceptors(failure).Options);
        var store = new PlanningPaycheckAllocationStore(db, new Clock());
        var foreign = Guid.NewGuid();
        await Save(store, complete, foreign, payroll, run, allocations);
        var foreignBefore = await store.GetForPaycheckAsync(foreign, payroll);
        var foreignRunBefore = await store.GetSavedPaycheckPlanAsync(foreign, payroll);
        var caller = new BankConnectionEntity { UserId = owner, InstitutionName = "Original caller bank" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Deleted caller bank" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Added caller bank" };
        db.AddRange(caller, deleted);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        caller = await db.BankConnections.SingleAsync(c => c.Id == caller.Id);
        deleted = await db.BankConnections.SingleAsync(c => c.Id == deleted.Id);
        var otherPayroll = Guid.NewGuid();
        if (callerChanges)
        {
            caller.InstitutionName = "Unsaved caller bank";
            db.Remove(deleted);
            db.Add(added);
            // Even pending rows in the same Planning tables belong to the
            // caller when they predate this target-paycheck operation.
            db.Add(new PlanningPaycheckPlanRunEntity
            {
                UserId = owner, PayrollTransactionId = otherPayroll,
                PaycheckPostedDate = Posted, PaycheckAmount = 100m, CurrencyCode = "USD",
                RecommendedSetAside = 10m, PaycheckRemainingAfterPlan = 90m, CreatedAtUtc = Now
            });
            db.Add(new PlanningPaycheckAllocationEntity
            {
                UserId = owner, PayrollTransactionId = otherPayroll,
                BillStreamId = Guid.NewGuid(), SourceStatementId = Guid.NewGuid(),
                PaycheckPostedDate = Posted, BillPeriodEnd = new DateOnly(2026, 9, 30),
                BillDueDate = new DateOnly(2026, 10, 15), PlannedAmount = 10m,
                CurrencyCode = "USD", CreatedAtUtc = Now
            });
        }
        var baseline = Capture(db);
        using var cancellation = new CancellationTokenSource();
        failure.Cancel = cancellation.Cancel;
        failure.Armed = true;
        bool? wasExisting = null;
        var error = await Record.ExceptionAsync(async () =>
        {
            wasExisting = await Save(store, complete, owner, payroll, run, allocations, cancellation.Token);
        });
        if (mode == "race-identical")
        {
            Assert.Null(error);
            if (complete) Assert.Equal(true, wasExisting);
        }
        else if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (mode == "unexpected") Assert.IsType<InvalidOperationException>(error);
        else Assert.IsType<DbUpdateException>(error);
        Assert.Equal(1, failure.Failures);
        Assert.True(failure.SawAllCandidates);
        Assert.Equal(baseline.Count, db.ChangeTracker.Entries().Count());
        foreach (var original in baseline)
        {
            var restored = Assert.Single(db.ChangeTracker.Entries(),
                e => ReferenceEquals(e.Entity, original.Entity));
            Assert.Equal(original.State, restored.State);
            Assert.Equal(original.Modified, restored.Properties.Where(p => p.IsModified)
                .Select(p => p.Metadata.Name).Order().ToArray());
            foreach (var (property, value) in original.Current) Assert.Equal(value, restored.CurrentValues[property]);
            foreach (var (property, value) in original.Original) Assert.Equal(value, restored.OriginalValues[property]);
        }

        // A later connection-health/monitoring save must not commit a failed
        // target plan, while retaining every preexisting caller change.
        await db.SaveChangesAsync();
        Assert.Equal(callerChanges ? "Unsaved caller bank" : "Original caller bank",
            (await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == caller.Id)).InstitutionName);
        Assert.Equal(!callerChanges, await db.BankConnections.AnyAsync(c => c.Id == deleted.Id));
        Assert.Equal(callerChanges, await db.BankConnections.AnyAsync(c => c.Id == added.Id));
        Assert.Equal(callerChanges ? 1 : 0, await db.PlanningPaycheckPlanRuns.CountAsync(r =>
            r.UserId == owner && r.PayrollTransactionId == otherPayroll));
        Assert.Equal(callerChanges ? 1 : 0, await db.PlanningPaycheckAllocations.CountAsync(a =>
            a.UserId == owner && a.PayrollTransactionId == otherPayroll));

        var race = mode.StartsWith("race-", StringComparison.Ordinal);
        var beforeRetry = await store.GetForPaycheckAsync(owner, payroll);
        var beforeRun = await store.GetSavedPaycheckPlanAsync(owner, payroll);
        Assert.Equal(race ? allocations.Length : 0, beforeRetry.Count);
        Assert.Equal(race && complete, beforeRun is not null);
        if (mode == "race-conflict")
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Save(store, complete, owner, payroll, run, allocations));
            Assert.Equal(beforeRetry, await store.GetForPaycheckAsync(owner, payroll));
            var afterRun = await store.GetSavedPaycheckPlanAsync(owner, payroll);
            Assert.Equal(beforeRun?.Run, afterRun?.Run);
        }
        else
        {
            var replay = await Save(store, complete, owner, payroll, run, allocations);
            if (complete) Assert.Equal(race, replay);
            var saved = await store.GetForPaycheckAsync(owner, payroll);
            Assert.Equal(allocations.Length, saved.Count);
            foreach (var draft in allocations)
            {
                var row = Assert.Single(saved, a => a.BillStreamId == draft.BillStreamId);
                Assert.Equal(draft.SourceStatementId, row.SourceStatementId);
                Assert.Equal(draft.PlannedAmount, row.PlannedAmount);
                Assert.Equal(draft.CurrencyCode, row.CurrencyCode);
                Assert.Equal(draft.BillDueDate, row.BillDueDate);
                Assert.Equal(draft.BillPeriodEnd, row.BillPeriodEnd);
            }
            if (complete)
            {
                var plan = await store.GetSavedPaycheckPlanAsync(owner, payroll);
                Assert.NotNull(plan);
                Assert.Equal(run.PaycheckAmount, plan.Run.PaycheckAmount);
                Assert.Equal(run.RecommendedSetAside, plan.Run.RecommendedSetAside);
                Assert.Equal(run.PaycheckRemainingAfterPlan, plan.Run.PaycheckRemainingAfterPlan);
                Assert.Equal(run.Shortfall, plan.Run.Shortfall);
                Assert.Equal(run.Coverage, plan.Run.Coverage);
            }
            await Save(store, complete, owner, payroll, run, allocations);
            Assert.Equal(saved, await store.GetForPaycheckAsync(owner, payroll));
        }
        Assert.Equal(foreignBefore, await store.GetForPaycheckAsync(foreign, payroll));
        var foreignRunAfter = await store.GetSavedPaycheckPlanAsync(foreign, payroll);
        Assert.Equal(foreignRunBefore?.Run, foreignRunAfter?.Run);
    }

    private static async Task<bool?> Save(PlanningPaycheckAllocationStore store, bool complete,
        Guid owner, Guid payroll, PlanningPaycheckPlanRunDraft run,
        IReadOnlyCollection<PlanningPaycheckAllocationDraft> allocations, CancellationToken token = default)
    {
        if (complete) return (await store.SaveCompletePaycheckPlanAsync(owner, payroll, run, allocations, token)).WasExisting;
        await store.SavePaycheckPlanAsync(owner, payroll, run.PaycheckPostedDate, allocations, token);
        return null;
    }

    private static List<TrackedState> Capture(FullWorthDbContext db) =>
        db.ChangeTracker.Entries().Select(e => new TrackedState(e.Entity, e.State,
            e.CurrentValues.Properties.ToDictionary(p => p.Name, p => e.CurrentValues[p]),
            e.OriginalValues.Properties.ToDictionary(p => p.Name, p => e.OriginalValues[p]),
            e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray())).ToList();

    private sealed record TrackedState(object Entity, EntityState State,
        Dictionary<string, object?> Current, Dictionary<string, object?> Original, string[] Modified);

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }

    private sealed class PlanFailure : SaveChangesInterceptor
    {
        public required DbContextOptions<FullWorthDbContext> PlainOptions { get; init; }
        public required string Mode { get; init; }
        public required bool Complete { get; init; }
        public required Guid Owner { get; init; }
        public required Guid Payroll { get; init; }
        public required PlanningPaycheckPlanRunDraft Run { get; init; }
        public required PlanningPaycheckAllocationDraft[] Allocations { get; init; }
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }
        public bool SawAllCandidates { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (!Armed) return result;
            Armed = false;
            Failures++;
            var db = eventData.Context!;
            SawAllCandidates = db.ChangeTracker.Entries<PlanningPaycheckPlanRunEntity>().Count(e =>
                e.State == EntityState.Added && e.Entity.UserId == Owner && e.Entity.PayrollTransactionId == Payroll) == (Complete ? 1 : 0) &&
                db.ChangeTracker.Entries<PlanningPaycheckAllocationEntity>().Count(e =>
                    e.State == EntityState.Added && e.Entity.UserId == Owner && e.Entity.PayrollTransactionId == Payroll) == Allocations.Length;
            if (Mode.StartsWith("race-", StringComparison.Ordinal))
            {
                await using var winnerContext = new FullWorthDbContext(PlainOptions);
                var winner = new PlanningPaycheckAllocationStore(winnerContext, new Clock());
                var winnerRun = Complete && Mode == "race-conflict"
                    ? Run with { Coverage = Run.Coverage! with { MissingDueDateCount = 1 } } : Run;
                var winnerAllocations = !Complete && Mode == "race-conflict"
                    ? Allocations.Select((a, index) => index == 0 ? a with { SourceStatementId = Guid.NewGuid() } : a).ToArray()
                    : Allocations;
                await Save(winner, Complete, Owner, Payroll, winnerRun, winnerAllocations);
            }
            if (Mode == "cancel")
            {
                Cancel!();
                token.ThrowIfCancellationRequested();
            }
            if (Mode == "unexpected") throw new InvalidOperationException("Synthetic pre-commit planning failure.");
            throw new DbUpdateException("Synthetic pre-commit/competing planning save failure.");
        }
    }
}

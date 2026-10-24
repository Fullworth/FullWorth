using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PaydayCoverageSnapshotTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Generation_FreezesCoverageAndMoney_AcrossChangedEvidence(
        bool includeEligible, bool includeSkipped)
    {
        await using var fixture = await Fixture.CreateAsync(includeEligible, includeSkipped);
        var first = await fixture.Service.GenerateAsync(fixture.UserId, fixture.PayrollId, Fixture.PostedDate);
        var coverage = Assert.IsType<PlanningPaycheckPlanCoverage>(first.Coverage);
        Assert.Equal(includeEligible ? 1 : 0, coverage.EligibleBillCount);
        Assert.Equal(includeSkipped ? 5 : 0, coverage.SkippedBillCount);
        Assert.Equal(includeSkipped ? 1 : 0, coverage.MissingStatementFactCount);
        Assert.Equal(includeSkipped ? 1 : 0, coverage.MissingDueDateCount);
        Assert.Equal(includeSkipped ? 1 : 0, coverage.CurrencyMismatchCount);
        Assert.Equal(includeSkipped ? 1 : 0, coverage.InvalidStatementAmountCount);
        Assert.Equal(includeSkipped ? 1 : 0, coverage.PriorCurrencyConflictCount);
        Assert.Equal(includeSkipped, coverage.HasExcludedBills);
        Assert.Equal((includeEligible ? 1 : 0) + (includeSkipped ? 5 : 0), coverage.EvaluatedBillCount);
        Assert.Equal(includeEligible ? 300m : 0m, first.RecommendedSetAside);
        Assert.Equal(includeSkipped ? 5 : 0, first.SkippedBills.Count);

        // Remove all current bill/evidence facts and make live payroll unreadable.
        fixture.Bills.Clear();
        fixture.Facts.Clear();
        fixture.Payroll.ThrowOnRead = true;
        fixture.Db.ChangeTracker.Clear();
        var replay = await fixture.Service.GenerateAsync(fixture.UserId, fixture.PayrollId, Fixture.PostedDate);
        Assert.True(replay.IsReplay);
        Assert.Equal(coverage, replay.Coverage);
        Assert.Equal(first.RecommendedSetAside, replay.RecommendedSetAside);
        Assert.Equal(first.PaycheckRemainingAfterPlan, replay.PaycheckRemainingAfterPlan);
        Assert.Equal(first.Shortfall, replay.Shortfall);
        Assert.Equal(first.Items.Select(item => (item.SourceStatementId, item.PlannedAmount)),
            replay.Items.Select(item => (item.SourceStatementId, item.PlannedAmount)));
        Assert.Empty(replay.SkippedBills); // Detailed current provider names are not historical evidence.

        var history = await fixture.Store.GetRecentPaycheckPlanHistoryAsync(fixture.UserId);
        Assert.Equal(coverage, history.Single(plan => plan.Run.PayrollTransactionId == fixture.PayrollId).Run.Coverage);
        Assert.Null(await fixture.Store.GetSavedPaycheckPlanAsync(Guid.NewGuid(), fixture.PayrollId));
        Assert.Empty(await fixture.Store.GetRecentPaycheckPlanHistoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task EligibleAlreadyFundedBill_IsEvaluatedEvenWithoutNewAllocation()
    {
        await using var fixture = await Fixture.CreateAsync(includeEligible: true, includeSkipped: false);
        var bill = Assert.Single(fixture.Bills);
        var fact = fixture.Facts[bill.BillStreamId];
        await fixture.Store.SavePaycheckPlanAsync(fixture.UserId, Guid.NewGuid(),
            Fixture.PostedDate.AddDays(-14),
            [new PlanningPaycheckAllocationDraft(bill.BillStreamId, fact.StatementId,
                fact.PeriodEnd, fact.DueDate!.Value, 600m, "USD")]);
        var plan = await fixture.Service.GenerateAsync(fixture.UserId, fixture.PayrollId, Fixture.PostedDate);
        Assert.Empty(plan.Items);
        Assert.Equal(0m, plan.RecommendedSetAside);
        var coverage = Assert.IsType<PlanningPaycheckPlanCoverage>(plan.Coverage);
        Assert.Equal(1, coverage.EligibleBillCount);
        Assert.Equal(1, coverage.EvaluatedBillCount);
        Assert.Equal(0, coverage.SkippedBillCount);
    }

    [Fact]
    public async Task LegacyPlan_RemainsUnknown_AndCannotBeBackfilledByReplay()
    {
        await using var fixture = await Fixture.CreateAsync(includeEligible: true, includeSkipped: false);
        var draft = EmptyDraft();
        var saved = await fixture.Store.SaveCompletePaycheckPlanAsync(
            fixture.UserId, fixture.PayrollId, draft, []);
        Assert.Null(saved.Run.Coverage);
        fixture.Payroll.ThrowOnRead = true;
        var replay = await fixture.Service.GenerateAsync(fixture.UserId, fixture.PayrollId, Fixture.PostedDate);
        Assert.True(replay.IsReplay);
        Assert.Null(replay.Coverage);
        Assert.Empty(replay.Items);
        Assert.Equal(0m, replay.RecommendedSetAside);
        Assert.Null(Assert.Single(await fixture.Store.GetRecentPaycheckPlanRunsAsync(fixture.UserId)).Coverage);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.SaveCompletePaycheckPlanAsync(fixture.UserId, fixture.PayrollId,
                draft with { Coverage = new PlanningPaycheckPlanCoverage(1, 0, 0, 0, 0, 0) }, []));
        Assert.Null((await fixture.Store.GetSavedPaycheckPlanAsync(fixture.UserId, fixture.PayrollId))!.Run.Coverage);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(501, 0)]
    [InlineData(500, 1)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public async Task InvalidCoverage_IsRejectedBeforePersistence(int eligible, int missing)
    {
        await using var fixture = await Fixture.CreateAsync(false, false);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Store.SaveCompletePaycheckPlanAsync(fixture.UserId, fixture.PayrollId,
                EmptyDraft() with { Coverage = new PlanningPaycheckPlanCoverage(eligible, missing, 0, 0, 0, 0) }, []));
        Assert.Empty(await fixture.Db.PlanningPaycheckPlanRuns.ToListAsync());
    }

    [Fact]
    public async Task SameMoneyWithDifferentCoverage_IsNotAnIdenticalSave()
    {
        await using var fixture = await Fixture.CreateAsync(false, false);
        var draft = EmptyDraft() with { Coverage = new PlanningPaycheckPlanCoverage(0, 1, 0, 0, 0, 0) };
        var first = await fixture.Store.SaveCompletePaycheckPlanAsync(fixture.UserId, fixture.PayrollId, draft, []);
        var retry = await fixture.Store.SaveCompletePaycheckPlanAsync(fixture.UserId, fixture.PayrollId, draft, []);
        Assert.True(retry.WasExisting);
        Assert.Equal(first.Run.Id, retry.Run.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.SaveCompletePaycheckPlanAsync(fixture.UserId, fixture.PayrollId,
                draft with { Coverage = new PlanningPaycheckPlanCoverage(1, 0, 0, 0, 0, 0) }, []));
        Assert.Equal(draft.Coverage,
            (await fixture.Store.GetSavedPaycheckPlanAsync(fixture.UserId, fixture.PayrollId))!.Run.Coverage);
        Assert.Single(await fixture.Db.PlanningPaycheckPlanRuns.ToListAsync());
    }

    [Fact]
    public async Task CoverageCannotExcludeEveryBill_WhenAnAllocationIsSaved()
    {
        await using var fixture = await Fixture.CreateAsync(false, false);
        var draft = new PlanningPaycheckPlanRunDraft(Fixture.PostedDate, 1000m, "USD", 100m, 900m, 0m)
        {
            Coverage = new PlanningPaycheckPlanCoverage(0, 1, 0, 0, 0, 0)
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Store.SaveCompletePaycheckPlanAsync(fixture.UserId, fixture.PayrollId, draft,
                [new PlanningPaycheckAllocationDraft(Guid.NewGuid(), Guid.NewGuid(),
                    new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 23), 100m, "USD")]));
        Assert.Empty(await fixture.Db.PlanningPaycheckPlanRuns.ToListAsync());
        Assert.Empty(await fixture.Db.PlanningPaycheckAllocations.ToListAsync());
    }

    private static PlanningPaycheckPlanRunDraft EmptyDraft() =>
        new(Fixture.PostedDate, 1000m, "USD", 0m, 1000m, 0m);

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly DateOnly PostedDate = new(2026, 9, 25);
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PayrollId { get; } = Guid.NewGuid();
        public FullWorthDbContext Db { get; } = new(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase($"payday-coverage-{Guid.NewGuid():N}").Options);
        public List<BillStreamReadRecord> Bills { get; } = [];
        public Dictionary<Guid, BillPlanningFact> Facts { get; } = [];
        public PlanningPaycheckAllocationStore Store { get; private set; } = null!;
        public PlanningPaydayPlanService Service { get; private set; } = null!;
        public PayrollGateway Payroll { get; private set; } = null!;

        public static async Task<Fixture> CreateAsync(bool includeEligible, bool includeSkipped)
        {
            var fixture = new Fixture();
            fixture.Store = new(fixture.Db, TimeProvider.System);
            fixture.Payroll = new(fixture.UserId, fixture.PayrollId);
            var bills = new BillsGateway(fixture.UserId, fixture.Bills);
            var settings = new PlanningSettingsService(fixture.Db, bills, TimeProvider.System);
            await settings.SavePayScheduleAsync(fixture.UserId, PayScheduleFrequency.Biweekly,
                PostedDate, null, 2);
            fixture.Service = new(settings, fixture.Store, bills,
                new FactsGateway(fixture.UserId, fixture.Facts), fixture.Payroll);
            if (includeEligible) fixture.AddBill();
            if (includeSkipped)
            {
                var missing = fixture.AddBill();
                fixture.Facts.Remove(missing);
                var noDue = fixture.AddBill();
                fixture.Facts[noDue] = fixture.Facts[noDue] with { DueDate = null };
                var currency = fixture.AddBill();
                fixture.Facts[currency] = fixture.Facts[currency] with { CurrencyCode = "CAD" };
                var invalid = fixture.AddBill();
                fixture.Facts[invalid] = fixture.Facts[invalid] with { Amount = -1m };
                var conflict = fixture.AddBill();
                var fact = fixture.Facts[conflict];
                await fixture.Store.SavePaycheckPlanAsync(fixture.UserId, Guid.NewGuid(),
                    PostedDate.AddDays(-14),
                    [new PlanningPaycheckAllocationDraft(conflict, fact.StatementId, fact.PeriodEnd,
                        fact.DueDate!.Value, 1m, "CAD")]);
            }
            return fixture;
        }

        private Guid AddBill()
        {
            var id = Guid.NewGuid();
            Bills.Add(new(id, "Synthetic bill", BillCategory.Internet));
            Facts[id] = new(id, Guid.NewGuid(), 600m, "USD",
                new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 15));
            return id;
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class BillsGateway(Guid owner, IReadOnlyList<BillStreamReadRecord> bills) : IBillStreamReadGateway
    {
        public Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillStreamReadRecord>>(userId == owner ? bills : []);
        public Task<BillStreamReadRecord?> GetOwnedAsync(Guid userId, Guid billStreamId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == owner ? bills.SingleOrDefault(b => b.BillStreamId == billStreamId) : null);
    }

    private sealed class FactsGateway(Guid owner, Dictionary<Guid, BillPlanningFact> facts) : IBillPlanningFactsGateway
    {
        public Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(Guid userId,
            IReadOnlyCollection<Guid> billStreamIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, BillPlanningFact>>(userId == owner
                ? facts.Where(pair => billStreamIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value)
                : new Dictionary<Guid, BillPlanningFact>());
    }

    private sealed class PayrollGateway(Guid owner, Guid payroll) : IPlanningPostedPayrollFactsGateway
    {
        public bool ThrowOnRead { get; set; }
        public Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(Guid userId,
            DateOnly fromInclusive, DateOnly throughInclusive, CancellationToken cancellationToken = default)
        {
            if (ThrowOnRead) throw new InvalidOperationException("Live payroll must not be read on replay.");
            return Task.FromResult<IReadOnlyList<PlanningPostedPayrollFact>>(userId == owner
                ? [new(payroll, 1000m, Fixture.PostedDate, "USD")] : []);
        }
    }
}

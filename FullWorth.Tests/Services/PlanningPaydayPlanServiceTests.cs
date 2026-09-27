using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlanningPaydayPlanServiceTests
{
    [Fact]
    public async Task GenerateAsync_PersistsDeterministicRecommendation_AndReplayIsStable()
    {
        var billStreamId =
            Guid.NewGuid();

        var statementId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                new BillPlanningFact(
                    billStreamId,
                    statementId,
                    600m,
                    "USD",
                    new DateOnly(
                        2026,
                        10,
                        23),
                    new DateOnly(
                        2026,
                        10,
                        15)),
                new PlanningPostedPayrollFact(
                    payrollTransactionId,
                    1000m,
                    new DateOnly(
                        2026,
                        9,
                        25),
                    "USD"));

        var first =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            PlanningPaydayPlanStatus.Ready,
            first.Status);

        Assert.False(
            first.IsReplay);

        Assert.Equal(
            300m,
            first.RecommendedSetAside);

        Assert.Equal(
            700m,
            first.PaycheckRemainingAfterPlan);

        Assert.Equal(
            0m,
            first.Shortfall);

        var item =
            Assert.Single(
                first.Items);

        Assert.Equal(
            billStreamId,
            item.BillStreamId);

        Assert.Equal(
            "Internet",
            item.ProviderName);

        Assert.Equal(
            statementId,
            item.SourceStatementId);

        Assert.Equal(
            300m,
            item.PlannedAmount);

        Assert.Empty(
            first.SkippedBills);

        Assert.Equal(
            1,
            await harness.DbContext
                .PlanningPaycheckAllocations
                .CountAsync());

        harness.BillFacts.Facts[billStreamId] =
            new BillPlanningFact(
                billStreamId,
                Guid.NewGuid(),
                900m,
                "USD",
                new DateOnly(
                    2026,
                    10,
                    23),
                new DateOnly(
                    2026,
                    10,
                    15));

        var replay =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.True(
            replay.IsReplay);

        Assert.Equal(
            300m,
            replay.RecommendedSetAside);

        Assert.Equal(
            statementId,
            Assert.Single(
                replay.Items)
                .SourceStatementId);

        Assert.Equal(
            1,
            await harness.DbContext
                .PlanningPaycheckAllocations
                .CountAsync());
    }

    [Fact]
    public async Task GenerateAsync_RequiresConfiguredPaySchedule()
    {
        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                Fact(
                    billStreamId),
                Payroll(
                    payrollTransactionId),
                configureSchedule: false);

        var result =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            PlanningPaydayPlanStatus.PayScheduleRequired,
            result.Status);

        Assert.Empty(
            result.Items);

        Assert.Equal(
            0,
            await harness.DbContext
                .PlanningPaycheckAllocations
                .CountAsync());
    }

    [Fact]
    public async Task GenerateAsync_SkipsBillWithoutDueDate_InsteadOfInventingOne()
    {
        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                new BillPlanningFact(
                    billStreamId,
                    Guid.NewGuid(),
                    600m,
                    "USD",
                    DueDate: null,
                    new DateOnly(
                        2026,
                        10,
                        15)),
                Payroll(
                    payrollTransactionId));

        var result =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            PlanningPaydayPlanStatus.Ready,
            result.Status);

        Assert.Equal(
            0m,
            result.RecommendedSetAside);

        Assert.Empty(
            result.Items);

        Assert.Equal(
            PlanningPaydayPlanSkipReason.MissingDueDate,
            Assert.Single(
                result.SkippedBills)
                .Reason);
    }

    [Fact]
    public async Task GenerateAsync_SkipsDifferentCurrency_WithoutFxArithmetic()
    {
        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                new BillPlanningFact(
                    billStreamId,
                    Guid.NewGuid(),
                    600m,
                    "CAD",
                    new DateOnly(
                        2026,
                        10,
                        23),
                    new DateOnly(
                        2026,
                        10,
                        15)),
                Payroll(
                    payrollTransactionId));

        var result =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            0m,
            result.RecommendedSetAside);

        Assert.Equal(
            PlanningPaydayPlanSkipReason.CurrencyMismatch,
            Assert.Single(
                result.SkippedBills)
                .Reason);

        Assert.Equal(
            0,
            await harness.DbContext
                .PlanningPaycheckAllocations
                .CountAsync());
    }

    [Fact]
    public async Task GenerateAsync_BillDecreaseBelowPriorPlan_NeedsNoNewAllocation()
    {
        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        var periodEnd =
            new DateOnly(
                2026,
                10,
                15);

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                new BillPlanningFact(
                    billStreamId,
                    Guid.NewGuid(),
                    350m,
                    "USD",
                    new DateOnly(
                        2026,
                        10,
                        23),
                    periodEnd),
                Payroll(
                    payrollTransactionId));

        await harness.Store.SavePaycheckPlanAsync(
            harness.UserId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                11),
            [
                new PlanningPaycheckAllocationDraft(
                    billStreamId,
                    Guid.NewGuid(),
                    periodEnd,
                    new DateOnly(
                        2026,
                        10,
                        23),
                    400m,
                    "USD")
            ]);

        var result =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            PlanningPaydayPlanStatus.Ready,
            result.Status);

        Assert.Equal(
            0m,
            result.RecommendedSetAside);

        Assert.Empty(
            result.Items);

        Assert.Equal(
            1,
            await harness.DbContext
                .PlanningPaycheckAllocations
                .CountAsync());
    }

    [Fact]
    public async Task GenerateAsync_DoesNotResolveAnotherUsersPayrollTransaction()
    {
        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                Fact(
                    billStreamId),
                Payroll(
                    payrollTransactionId));

        var result =
            await harness.Service.GenerateAsync(
                Guid.NewGuid(),
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            PlanningPaydayPlanStatus.PayrollNotFound,
            result.Status);

        Assert.Empty(
            result.Items);
    }

    [Fact]
    public async Task GenerateAsync_RejectsUnsupportedPayrollCurrencyWithoutPlanning()
    {
        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billStreamId,
                Fact(
                    billStreamId),
                new PlanningPostedPayrollFact(
                    payrollTransactionId,
                    1000m,
                    new DateOnly(
                        2026,
                        9,
                        25),
                    CurrencyCode: null));

        var result =
            await harness.Service.GenerateAsync(
                harness.UserId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25));

        Assert.Equal(
            PlanningPaydayPlanStatus.PayrollFactUnsupported,
            result.Status);

        Assert.Empty(
            result.Items);

        Assert.Equal(
            0,
            await harness.DbContext
                .PlanningPaycheckAllocations
                .CountAsync());
    }

    private static BillPlanningFact Fact(
        Guid billStreamId) =>
        new(
            billStreamId,
            Guid.NewGuid(),
            600m,
            "USD",
            new DateOnly(
                2026,
                10,
                23),
            new DateOnly(
                2026,
                10,
                15));

    private static PlanningPostedPayrollFact Payroll(
        Guid transactionId) =>
        new(
            transactionId,
            1000m,
            new DateOnly(
                2026,
                9,
                25),
            "USD");

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            Guid userId,
            FullWorthDbContext dbContext,
            PlanningPaycheckAllocationStore store,
            PlanningPaydayPlanService service,
            FakeBillPlanningFactsGateway billFacts)
        {
            UserId =
                userId;

            DbContext =
                dbContext;

            Store =
                store;

            Service =
                service;

            BillFacts =
                billFacts;
        }

        public Guid UserId { get; }

        public FullWorthDbContext DbContext { get; }

        public PlanningPaycheckAllocationStore Store { get; }

        public PlanningPaydayPlanService Service { get; }

        public FakeBillPlanningFactsGateway BillFacts { get; }

        public static async Task<Harness> CreateAsync(
            Guid billStreamId,
            BillPlanningFact billFact,
            PlanningPostedPayrollFact payrollFact,
            bool configureSchedule = true)
        {
            var userId =
                Guid.NewGuid();

            var options =
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"planning-payday-plan-{Guid.NewGuid():N}")
                    .Options;

            var dbContext =
                new FullWorthDbContext(
                    options);

            var billGateway =
                new FakeBillStreamReadGateway(
                    userId,
                    [
                        new BillStreamReadRecord(
                            billStreamId,
                            "Internet",
                            BillCategory.Internet)
                    ]);

            var billFacts =
                new FakeBillPlanningFactsGateway(
                    userId,
                    new Dictionary<Guid, BillPlanningFact>
                    {
                        [billStreamId] =
                            billFact
                    });

            var payrollFacts =
                new FakePayrollFactsGateway(
                    userId,
                    [
                        payrollFact
                    ]);

            var settings =
                new PlanningSettingsService(
                    dbContext,
                    billGateway,
                    TimeProvider.System);

            if (configureSchedule)
            {
                await settings.SavePayScheduleAsync(
                    userId,
                    PayScheduleFrequency.Biweekly,
                    new DateOnly(
                        2026,
                        9,
                        25),
                    secondaryDayOfMonth: null,
                    defaultPaychecksAhead: 2);
            }

            var store =
                new PlanningPaycheckAllocationStore(
                    dbContext,
                    TimeProvider.System);

            var service =
                new PlanningPaydayPlanService(
                    settings,
                    store,
                    billGateway,
                    billFacts,
                    payrollFacts);

            return new Harness(
                userId,
                dbContext,
                store,
                service,
                billFacts);
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
        }
    }

    private sealed class FakeBillStreamReadGateway(
        Guid ownerUserId,
        IReadOnlyList<BillStreamReadRecord> bills)
        : IBillStreamReadGateway
    {
        public Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillStreamReadRecord>>(
                userId ==
                    ownerUserId
                    ? bills
                    : []);

        public Task<BillStreamReadRecord?> GetOwnedAsync(
            Guid userId,
            Guid billStreamId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                userId ==
                    ownerUserId
                    ? bills.SingleOrDefault(
                        bill =>
                            bill.BillStreamId ==
                            billStreamId)
                    : null);
    }

    private sealed class FakeBillPlanningFactsGateway(
        Guid ownerUserId,
        Dictionary<Guid, BillPlanningFact> facts)
        : IBillPlanningFactsGateway
    {
        public Dictionary<Guid, BillPlanningFact> Facts { get; } =
            facts;

        public Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            CancellationToken cancellationToken = default)
        {
            if (userId !=
                ownerUserId)
            {
                return Task.FromResult<IReadOnlyDictionary<Guid, BillPlanningFact>>(
                    new Dictionary<Guid, BillPlanningFact>());
            }

            IReadOnlyDictionary<Guid, BillPlanningFact> result =
                Facts
                    .Where(
                        pair =>
                            billStreamIds.Contains(
                                pair.Key))
                    .ToDictionary();

            return Task.FromResult(
                result);
        }
    }

    private sealed class FakePayrollFactsGateway(
        Guid ownerUserId,
        IReadOnlyList<PlanningPostedPayrollFact> facts)
        : IPlanningPostedPayrollFactsGateway
    {
        public Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(
            Guid userId,
            DateOnly fromInclusive,
            DateOnly throughInclusive,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PlanningPostedPayrollFact> result =
                userId ==
                    ownerUserId
                    ? facts
                        .Where(
                            fact =>
                                fact.PostedDate >=
                                    fromInclusive &&
                                fact.PostedDate <=
                                    throughInclusive)
                        .ToList()
                    : [];

            return Task.FromResult(
                result);
        }
    }
}

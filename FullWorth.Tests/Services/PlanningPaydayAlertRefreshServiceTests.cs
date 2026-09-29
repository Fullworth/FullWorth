using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlanningPaydayAlertRefreshServiceTests
{
    [Fact]
    public async Task RefreshAsync_EnsuresRecentReadyPlanAlert_AndReplayDoesNotDuplicate()
    {
        await using var harness =
            await Harness.CreateAsync();

        var first =
            await harness.RefreshService.RefreshAsync(
                harness.UserId);

        Assert.Equal(
            1,
            first.PayrollFactsScanned);

        Assert.Equal(
            1,
            first.ReadyPlansEnsured);

        Assert.Equal(
            0,
            first.SkippedFacts);

        Assert.False(
            first.PayScheduleRequired);

        var alert =
            Assert.Single(
                await harness.DbContext.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());

        Assert.Equal(
            BillAlertType.PaydayPlan,
            alert.AlertType);

        Assert.Equal(
            harness.PayrollTransactionId,
            alert.SourceEventId);

        Assert.Equal(
            BillAlertSeverity.Info,
            alert.Severity);

        Assert.Equal(
            "Payday plan ready",
            alert.Title);

        Assert.Equal(
            "FullWorth recommends planning 300.00 USD of this 1000.00 USD paycheck for upcoming bills, leaving 700.00 USD unplanned.",
            alert.Message);

        Assert.Equal(
            1,
            await harness.DbContext
                .PlanningPaycheckPlanRuns
                .CountAsync());

        var second =
            await harness.RefreshService.RefreshAsync(
                harness.UserId);

        Assert.Equal(
            1,
            second.ReadyPlansEnsured);

        Assert.Equal(
            1,
            await harness.DbContext.BillAlerts
                .CountAsync());

        Assert.Equal(
            1,
            await harness.DbContext
                .PlanningPaycheckPlanRuns
                .CountAsync());
    }

    [Fact]
    public async Task RefreshAsync_UsesBoundedSevenDayPayrollWindow()
    {
        var oldPayroll =
            new PlanningPostedPayrollFact(
                Guid.NewGuid(),
                900m,
                new DateOnly(
                    2026,
                    9,
                    19),
                "USD");

        await using var harness =
            await Harness.CreateAsync(
                payrollFacts:
                    [
                        oldPayroll
                    ]);

        var result =
            await harness.RefreshService.RefreshAsync(
                harness.UserId);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                20),
            harness.PayrollFacts.LastFromInclusive);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                27),
            harness.PayrollFacts.LastThroughInclusive);

        Assert.Equal(
            0,
            result.PayrollFactsScanned);

        Assert.Empty(
            await harness.DbContext.BillAlerts
                .ToListAsync());
    }

    [Fact]
    public async Task RefreshAsync_MissingPayScheduleStopsWithoutCreatingAlert()
    {
        await using var harness =
            await Harness.CreateAsync(
                configureSchedule:
                    false);

        var result =
            await harness.RefreshService.RefreshAsync(
                harness.UserId);

        Assert.Equal(
            1,
            result.PayrollFactsScanned);

        Assert.Equal(
            0,
            result.ReadyPlansEnsured);

        Assert.True(
            result.PayScheduleRequired);

        Assert.Empty(
            await harness.DbContext.BillAlerts
                .ToListAsync());

        Assert.Empty(
            await harness.DbContext
                .PlanningPaycheckPlanRuns
                .ToListAsync());
    }

    [Fact]
    public async Task RefreshAsync_UnsupportedPayrollFactIsSkippedWithoutAlert()
    {
        await using var harness =
            await Harness.CreateAsync(
                payrollFacts:
                    [
                        new PlanningPostedPayrollFact(
                            Guid.NewGuid(),
                            1000m,
                            new DateOnly(
                                2026,
                                9,
                                25),
                            CurrencyCode:
                                null)
                    ]);

        var result =
            await harness.RefreshService.RefreshAsync(
                harness.UserId);

        Assert.Equal(
            1,
            result.PayrollFactsScanned);

        Assert.Equal(
            0,
            result.ReadyPlansEnsured);

        Assert.Equal(
            1,
            result.SkippedFacts);

        Assert.False(
            result.PayScheduleRequired);

        Assert.Empty(
            await harness.DbContext.BillAlerts
                .ToListAsync());
    }

    [Fact]
    public async Task RefreshAsync_ZeroAllocationUsesEvidenceBoundedMessage()
    {
        var billStreamId =
            Guid.NewGuid();

        await using var harness =
            await Harness.CreateAsync(
                billFact:
                    new BillPlanningFact(
                        billStreamId,
                        Guid.NewGuid(),
                        600m,
                        "USD",
                        DueDate:
                            null,
                        new DateOnly(
                            2026,
                            10,
                            15)),
                billStreamId:
                    billStreamId);

        var result =
            await harness.RefreshService.RefreshAsync(
                harness.UserId);

        Assert.Equal(
            1,
            result.ReadyPlansEnsured);

        var alert =
            Assert.Single(
                await harness.DbContext.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());

        Assert.Equal(
            "No bill allocation is recommended from this 1000.00 USD paycheck based on the bill evidence currently available.",
            alert.Message);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            Guid userId,
            Guid payrollTransactionId,
            FullWorthDbContext dbContext,
            FakePayrollFactsGateway payrollFacts,
            PlanningPaydayAlertRefreshService refreshService)
        {
            UserId =
                userId;

            PayrollTransactionId =
                payrollTransactionId;

            DbContext =
                dbContext;

            PayrollFacts =
                payrollFacts;

            RefreshService =
                refreshService;
        }

        public Guid UserId { get; }

        public Guid PayrollTransactionId { get; }

        public FullWorthDbContext DbContext { get; }

        public FakePayrollFactsGateway PayrollFacts { get; }

        public PlanningPaydayAlertRefreshService RefreshService { get; }

        public static async Task<Harness> CreateAsync(
            bool configureSchedule = true,
            Guid? billStreamId = null,
            BillPlanningFact? billFact = null,
            IReadOnlyList<PlanningPostedPayrollFact>? payrollFacts = null)
        {
            var userId =
                Guid.NewGuid();

            var effectiveBillStreamId =
                billStreamId ??
                Guid.NewGuid();

            var payrollTransactionId =
                payrollFacts?
                    .FirstOrDefault()?
                    .TransactionId ??
                Guid.NewGuid();

            var options =
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"planning-payday-alert-refresh-{Guid.NewGuid():N}")
                    .Options;

            var dbContext =
                new FullWorthDbContext(
                    options);

            var billGateway =
                new FakeBillStreamReadGateway(
                    userId,
                    [
                        new BillStreamReadRecord(
                            effectiveBillStreamId,
                            "Internet",
                            BillCategory.Internet)
                    ]);

            var effectiveBillFact =
                billFact ??
                new BillPlanningFact(
                    effectiveBillStreamId,
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

            var billFacts =
                new FakeBillPlanningFactsGateway(
                    userId,
                    new Dictionary<Guid, BillPlanningFact>
                    {
                        [effectiveBillStreamId] =
                            effectiveBillFact
                    });

            var effectivePayrollFacts =
                payrollFacts ??
                [
                    new PlanningPostedPayrollFact(
                        payrollTransactionId,
                        1000m,
                        new DateOnly(
                            2026,
                            9,
                            25),
                        "USD")
                ];

            var payrollGateway =
                new FakePayrollFactsGateway(
                    userId,
                    effectivePayrollFacts);

            var timeProvider =
                new FixedTimeProvider(
                    new DateTimeOffset(
                        2026,
                        9,
                        27,
                        12,
                        0,
                        0,
                        TimeSpan.Zero));

            var settings =
                new PlanningSettingsService(
                    dbContext,
                    billGateway,
                    timeProvider);

            if (configureSchedule)
            {
                await settings.SavePayScheduleAsync(
                    userId,
                    PayScheduleFrequency.Biweekly,
                    new DateOnly(
                        2026,
                        9,
                        25),
                    secondaryDayOfMonth:
                        null,
                    defaultPaychecksAhead:
                        2);
            }

            var store =
                new PlanningPaycheckAllocationStore(
                    dbContext,
                    timeProvider);

            var paydayPlanService =
                new PlanningPaydayPlanService(
                    settings,
                    store,
                    billGateway,
                    billFacts,
                    payrollGateway);

            var alertGateway =
                new PlanningPaydayAlertGateway(
                    dbContext);

            var refreshService =
                new PlanningPaydayAlertRefreshService(
                    paydayPlanService,
                    payrollGateway,
                    alertGateway,
                    timeProvider);

            return new Harness(
                userId,
                payrollTransactionId,
                dbContext,
                payrollGateway,
                refreshService);
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
        IReadOnlyDictionary<Guid, BillPlanningFact> facts)
        : IBillPlanningFactsGateway
    {
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
                facts
                    .Where(
                        pair =>
                            billStreamIds.Contains(
                                pair.Key))
                    .ToDictionary(
                        pair =>
                            pair.Key,
                        pair =>
                            pair.Value);

            return Task.FromResult(
                result);
        }
    }

    private sealed class FakePayrollFactsGateway(
        Guid ownerUserId,
        IReadOnlyList<PlanningPostedPayrollFact> facts)
        : IPlanningPostedPayrollFactsGateway
    {
        public DateOnly? LastFromInclusive { get; private set; }

        public DateOnly? LastThroughInclusive { get; private set; }

        public Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(
            Guid userId,
            DateOnly fromInclusive,
            DateOnly throughInclusive,
            CancellationToken cancellationToken = default)
        {
            LastFromInclusive =
                fromInclusive;

            LastThroughInclusive =
                throughInclusive;

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

    private sealed class FixedTimeProvider(
        DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            utcNow;
    }
}

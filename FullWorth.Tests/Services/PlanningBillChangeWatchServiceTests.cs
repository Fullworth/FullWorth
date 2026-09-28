using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlanningBillChangeWatchServiceTests
{
    [Fact]
    public async Task GetAsync_RecalculatesRemainingAmountFromConfirmedLatestChange()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var statementId =
            Guid.NewGuid();

        var periodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await using var harness =
            Harness.Create(
                userId,
                billStreamId,
                statementId,
                periodEnd,
                currentAmount:
                    600m,
                previousAmount:
                    500m);

        await harness.Store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                25),
            [
                new PlanningPaycheckAllocationDraft(
                    billStreamId,
                    statementId,
                    periodEnd,
                    new DateOnly(
                        2026,
                        11,
                        15),
                    200m,
                    "USD")
            ]);

        var result =
            await harness.Service.GetAsync(
                userId);

        var item =
            Assert.Single(
                result);

        Assert.Equal(
            100m,
            item.AmountDifference);

        Assert.Equal(
            1200m,
            item.AnnualizedImpact);

        Assert.Equal(
            200m,
            item.AlreadyPlanned);

        Assert.Equal(
            400m,
            item.RemainingAmountToPlan);

        Assert.Equal(
            PlanningBillChangeWatchRecalculationStatus.Ready,
            item.RecalculationStatus);
    }

    [Fact]
    public async Task GetAsync_BillDecreaseBelowPriorPlan_ClampsRemainingToZero()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var statementId =
            Guid.NewGuid();

        var periodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await using var harness =
            Harness.Create(
                userId,
                billStreamId,
                statementId,
                periodEnd,
                currentAmount:
                    150m,
                previousAmount:
                    300m);

        await harness.Store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                25),
            [
                new PlanningPaycheckAllocationDraft(
                    billStreamId,
                    statementId,
                    periodEnd,
                    new DateOnly(
                        2026,
                        11,
                        15),
                    200m,
                    "USD")
            ]);

        var item =
            Assert.Single(
                await harness.Service.GetAsync(
                    userId));

        Assert.Equal(
            -150m,
            item.AmountDifference);

        Assert.Equal(
            200m,
            item.AlreadyPlanned);

        Assert.Equal(
            0m,
            item.RemainingAmountToPlan);
    }

    [Fact]
    public async Task GetAsync_DoesNotSurfaceStaleChangeForOlderStatement()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var latestStatementId =
            Guid.NewGuid();

        var periodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await using var harness =
            Harness.Create(
                userId,
                billStreamId,
                latestStatementId,
                periodEnd,
                currentAmount:
                    600m,
                previousAmount:
                    500m,
                changeCurrentStatementId:
                    Guid.NewGuid());

        var result =
            await harness.Service.GetAsync(
                userId);

        Assert.Empty(
            result);
    }

    [Fact]
    public async Task GetAsync_CurrencyConflictFailsClosedForRecalculation()
    {
        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var statementId =
            Guid.NewGuid();

        var periodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await using var harness =
            Harness.Create(
                userId,
                billStreamId,
                statementId,
                periodEnd,
                currentAmount:
                    600m,
                previousAmount:
                    500m);

        await harness.Store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                25),
            [
                new PlanningPaycheckAllocationDraft(
                    billStreamId,
                    statementId,
                    periodEnd,
                    new DateOnly(
                        2026,
                        11,
                        15),
                    200m,
                    "CAD")
            ]);

        var item =
            Assert.Single(
                await harness.Service.GetAsync(
                    userId));

        Assert.Equal(
            PlanningBillChangeWatchRecalculationStatus.PriorCurrencyConflict,
            item.RecalculationStatus);

        Assert.Null(
            item.AlreadyPlanned);

        Assert.Null(
            item.RemainingAmountToPlan);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(
            FullWorthDbContext dbContext,
            PlanningPaycheckAllocationStore store,
            PlanningBillChangeWatchService service)
        {
            DbContext =
                dbContext;

            Store =
                store;

            Service =
                service;
        }

        public FullWorthDbContext DbContext { get; }

        public PlanningPaycheckAllocationStore Store { get; }

        public PlanningBillChangeWatchService Service { get; }

        public static Harness Create(
            Guid userId,
            Guid billStreamId,
            Guid statementId,
            DateOnly periodEnd,
            decimal currentAmount,
            decimal previousAmount,
            Guid? changeCurrentStatementId = null)
        {
            var options =
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"planning-change-watch-{Guid.NewGuid():N}")
                    .Options;

            var dbContext =
                new FullWorthDbContext(
                    options);

            var store =
                new PlanningPaycheckAllocationStore(
                    dbContext,
                    TimeProvider.System);

            var billGateway =
                new FakeBillStreamReadGateway(
                    userId,
                    [
                        new BillStreamReadRecord(
                            billStreamId,
                            "Internet",
                            BillCategory.Internet)
                    ]);

            var currentFact =
                new BillPlanningFact(
                    billStreamId,
                    statementId,
                    currentAmount,
                    "USD",
                    new DateOnly(
                        2026,
                        11,
                        15),
                    periodEnd);

            var changeFact =
                new BillPlanningChangeFact(
                    billStreamId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    changeCurrentStatementId ??
                        statementId,
                    previousAmount,
                    currentAmount,
                    currentAmount -
                        previousAmount,
                    (currentAmount -
                        previousAmount) *
                        12m,
                    "Statement evidence explains the confirmed total change.",
                    periodEnd);

            var service =
                new PlanningBillChangeWatchService(
                    billGateway,
                    new FakeBillPlanningFactsGateway(
                        userId,
                        new Dictionary<Guid, BillPlanningFact>
                        {
                            [billStreamId] =
                                currentFact
                        }),
                    new FakeBillPlanningChangeFactsGateway(
                        userId,
                        new Dictionary<Guid, BillPlanningChangeFact>
                        {
                            [billStreamId] =
                                changeFact
                        }),
                    store);

            return new Harness(
                dbContext,
                store,
                service);
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
            IReadOnlyDictionary<Guid, BillPlanningFact> result =
                userId ==
                    ownerUserId
                    ? facts
                        .Where(
                            pair =>
                                billStreamIds.Contains(
                                    pair.Key))
                        .ToDictionary(
                            pair =>
                                pair.Key,
                            pair =>
                                pair.Value)
                    : new Dictionary<Guid, BillPlanningFact>();

            return Task.FromResult(
                result);
        }
    }

    private sealed class FakeBillPlanningChangeFactsGateway(
        Guid ownerUserId,
        IReadOnlyDictionary<Guid, BillPlanningChangeFact> facts)
        : IBillPlanningChangeFactsGateway
    {
        public Task<IReadOnlyDictionary<Guid, BillPlanningChangeFact>>
            GetLatestConfirmedAsync(
                Guid userId,
                IReadOnlyCollection<Guid> billStreamIds,
                CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<Guid, BillPlanningChangeFact> result =
                userId ==
                    ownerUserId
                    ? facts
                        .Where(
                            pair =>
                                billStreamIds.Contains(
                                    pair.Key))
                        .ToDictionary(
                            pair =>
                                pair.Key,
                            pair =>
                                pair.Value)
                    : new Dictionary<Guid, BillPlanningChangeFact>();

            return Task.FromResult(
                result);
        }
    }
}

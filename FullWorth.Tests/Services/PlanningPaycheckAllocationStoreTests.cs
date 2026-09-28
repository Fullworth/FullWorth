using FullWorth.API.Data;
using FullWorth.API.Services.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlanningPaycheckAllocationStoreTests
{
    [Fact]
    public async Task SavePaycheckPlanAsync_IsIdempotentForIdenticalReplay()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var userId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        var draft =
            Draft(
                plannedAmount: 125m);

        var first =
            await store.SavePaycheckPlanAsync(
                userId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25),
                [draft]);

        var replay =
            await store.SavePaycheckPlanAsync(
                userId,
                payrollTransactionId,
                new DateOnly(
                    2026,
                    9,
                    25),
                [draft]);

        Assert.Single(
            first);

        Assert.Single(
            replay);

        Assert.Equal(
            first[0].Id,
            replay[0].Id);

        Assert.Equal(
            1,
            await dbContext
                .PlanningPaycheckAllocations
                .CountAsync());
    }

    [Fact]
    public async Task SavePaycheckPlanAsync_RejectsDifferentReplay()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var userId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        var original =
            Draft(
                plannedAmount: 125m);

        await store.SavePaycheckPlanAsync(
            userId,
            payrollTransactionId,
            new DateOnly(
                2026,
                9,
                25),
            [original]);

        var changed =
            original with
            {
                PlannedAmount =
                    150m
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                store.SavePaycheckPlanAsync(
                    userId,
                    payrollTransactionId,
                    new DateOnly(
                        2026,
                        9,
                        25),
                    [changed]));

        var persisted =
            await store.GetForPaycheckAsync(
                userId,
                payrollTransactionId);

        Assert.Equal(
            125m,
            Assert.Single(
                persisted)
                .PlannedAmount);
    }

    [Fact]
    public async Task GetForPaycheckAsync_IsOwnershipScoped()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var payrollTransactionId =
            Guid.NewGuid();

        var firstUserId =
            Guid.NewGuid();

        var secondUserId =
            Guid.NewGuid();

        await store.SavePaycheckPlanAsync(
            firstUserId,
            payrollTransactionId,
            new DateOnly(
                2026,
                9,
                25),
            [
                Draft(
                    plannedAmount: 100m)
            ]);

        await store.SavePaycheckPlanAsync(
            secondUserId,
            payrollTransactionId,
            new DateOnly(
                2026,
                9,
                25),
            [
                Draft(
                    plannedAmount: 900m)
            ]);

        var firstUser =
            await store.GetForPaycheckAsync(
                firstUserId,
                payrollTransactionId);

        var secondUser =
            await store.GetForPaycheckAsync(
                secondUserId,
                payrollTransactionId);

        Assert.Equal(
            100m,
            Assert.Single(
                firstUser)
                .PlannedAmount);

        Assert.Equal(
            900m,
            Assert.Single(
                secondUser)
                .PlannedAmount);
    }

    [Fact]
    public async Task GetPriorTotalsAsync_IncludesSameDayEarlierPlans_AndExcludesCurrentPayroll()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var billPeriodEnd =
            new DateOnly(
                2026,
                10,
                31);

        var firstPayrollTransactionId =
            Guid.NewGuid();

        var currentPayrollTransactionId =
            Guid.NewGuid();

        await store.SavePaycheckPlanAsync(
            userId,
            firstPayrollTransactionId,
            new DateOnly(
                2026,
                9,
                25),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        100m)
            ]);

        await store.SavePaycheckPlanAsync(
            userId,
            currentPayrollTransactionId,
            new DateOnly(
                2026,
                9,
                25),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        150m)
            ]);

        var totals =
            await store.GetPriorTotalsAsync(
                userId,
                [billStreamId],
                new DateOnly(
                    2026,
                    9,
                    25),
                currentPayrollTransactionId);

        var total =
            Assert.Single(
                totals);

        Assert.Equal(
            billStreamId,
            total.BillStreamId);

        Assert.Equal(
            billPeriodEnd,
            total.BillPeriodEnd);

        Assert.Equal(
            100m,
            total.PlannedAmount);
    }

    [Fact]
    public async Task GetPriorTotalsAsync_DoesNotLeakOtherUsersOrFuturePaychecks()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var billPeriodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                10),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        80m)
            ]);

        await store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                10,
                1),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        200m)
            ]);

        await store.SavePaycheckPlanAsync(
            otherUserId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                10),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        999m)
            ]);

        var totals =
            await store.GetPriorTotalsAsync(
                userId,
                [billStreamId],
                new DateOnly(
                    2026,
                    9,
                    30),
                Guid.NewGuid());

        Assert.Equal(
            80m,
            Assert.Single(
                totals)
                .PlannedAmount);
    }

    [Fact]
    public async Task SavePaycheckPlanAsync_RejectsDuplicateBillCycle()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var billStreamId =
            Guid.NewGuid();

        var billPeriodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                store.SavePaycheckPlanAsync(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    new DateOnly(
                        2026,
                        9,
                        25),
                    [
                        Draft(
                            billStreamId:
                                billStreamId,
                            billPeriodEnd:
                                billPeriodEnd,
                            plannedAmount:
                                100m),
                        Draft(
                            billStreamId:
                                billStreamId,
                            billPeriodEnd:
                                billPeriodEnd,
                            plannedAmount:
                                75m)
                    ]));
    }

    [Fact]
    public async Task SavePaycheckPlanAsync_NormalizesCurrency()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var result =
            await store.SavePaycheckPlanAsync(
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateOnly(
                    2026,
                    9,
                    25),
                [
                    Draft(
                        plannedAmount:
                            50m,
                        currencyCode:
                            " usd ")
                ]);

        Assert.Equal(
            "USD",
            Assert.Single(
                result)
                .CurrencyCode);
    }

    [Fact]
    public async Task GetCycleTotalsAsync_IsOwnershipScoped_AndAggregatesSameBillCycle()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var billPeriodEnd =
            new DateOnly(
                2026,
                10,
                31);

        await store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                11),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        80m)
            ]);

        await store.SavePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                25),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        120m)
            ]);

        await store.SavePaycheckPlanAsync(
            otherUserId,
            Guid.NewGuid(),
            new DateOnly(
                2026,
                9,
                25),
            [
                Draft(
                    billStreamId:
                        billStreamId,
                    billPeriodEnd:
                        billPeriodEnd,
                    plannedAmount:
                        999m)
            ]);

        var totals =
            await store.GetCycleTotalsAsync(
                userId,
                [billStreamId]);

        var total =
            Assert.Single(
                totals);

        Assert.Equal(
            billStreamId,
            total.BillStreamId);

        Assert.Equal(
            billPeriodEnd,
            total.BillPeriodEnd);

        Assert.Equal(
            "USD",
            total.CurrencyCode);

        Assert.Equal(
            200m,
            total.PlannedAmount);
    }

    private static DbContextOptions<FullWorthDbContext> Options() =>
        new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(
                $"planning-allocation-store-{Guid.NewGuid():N}")
            .Options;

    private static PlanningPaycheckAllocationDraft Draft(
        decimal plannedAmount,
        string currencyCode = "USD",
        Guid? billStreamId = null,
        Guid? sourceStatementId = null,
        DateOnly? billPeriodEnd = null,
        DateOnly? billDueDate = null) =>
        new(
            billStreamId ??
                Guid.NewGuid(),
            sourceStatementId ??
                Guid.NewGuid(),
            billPeriodEnd ??
                new DateOnly(
                    2026,
                    10,
                    31),
            billDueDate ??
                new DateOnly(
                    2026,
                    11,
                    15),
            plannedAmount,
            currencyCode);
}

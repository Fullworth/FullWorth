using FullWorth.API.Data;
using FullWorth.API.Services.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlanningRecentPaycheckPlanTests
{
    [Fact]
    public async Task GetRecentPaycheckPlanRunsAsync_IsOwnerScopedNewestFirstAndBounded()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-recent-runs-{Guid.NewGuid():N}")
                .Options;

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

        await SaveZeroPlanAsync(
            store,
            userId,
            new DateOnly(
                2026,
                9,
                12),
            900m);

        var latestPayrollId =
            Guid.NewGuid();

        await store.SaveCompletePaycheckPlanAsync(
            userId,
            latestPayrollId,
            new PlanningPaycheckPlanRunDraft(
                new DateOnly(
                    2026,
                    9,
                    26),
                PaycheckAmount: 1000m,
                CurrencyCode: "USD",
                RecommendedSetAside: 1200m,
                PaycheckRemainingAfterPlan: 0m,
                Shortfall: 200m),
            [
                new PlanningPaycheckAllocationDraft(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    new DateOnly(
                        2026,
                        10,
                        31),
                    new DateOnly(
                        2026,
                        10,
                        15),
                    PlannedAmount: 1200m,
                    CurrencyCode: "USD")
            ]);

        await SaveZeroPlanAsync(
            store,
            otherUserId,
            new DateOnly(
                2026,
                9,
                27),
            5000m);

        var result =
            await store.GetRecentPaycheckPlanRunsAsync(
                userId,
                take: 1);

        var run =
            Assert.Single(
                result);

        Assert.Equal(
            latestPayrollId,
            run.PayrollTransactionId);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                26),
            run.PaycheckPostedDate);

        Assert.Equal(
            1000m,
            run.PaycheckAmount);

        Assert.Equal(
            1200m,
            run.RecommendedSetAside);

        Assert.Equal(
            0m,
            run.PaycheckRemainingAfterPlan);

        Assert.Equal(
            200m,
            run.Shortfall);

        Assert.Equal(
            "USD",
            run.CurrencyCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task GetRecentPaycheckPlanRunsAsync_RejectsUnboundedTake(
        int take)
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-recent-runs-validation-{Guid.NewGuid():N}")
                .Options;

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var store =
            new PlanningPaycheckAllocationStore(
                dbContext,
                TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
                store.GetRecentPaycheckPlanRunsAsync(
                    Guid.NewGuid(),
                    take));
    }

    private static async Task SaveZeroPlanAsync(
        PlanningPaycheckAllocationStore store,
        Guid userId,
        DateOnly postedDate,
        decimal paycheckAmount)
    {
        await store.SaveCompletePaycheckPlanAsync(
            userId,
            Guid.NewGuid(),
            new PlanningPaycheckPlanRunDraft(
                postedDate,
                paycheckAmount,
                "USD",
                RecommendedSetAside: 0m,
                PaycheckRemainingAfterPlan:
                    paycheckAmount,
                Shortfall: 0m),
            allocations: []);
    }
}

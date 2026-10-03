using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class UserOwnedResourceIsolationTests
{
    [Fact]
    public async Task SubscriptionStatus_DoesNotExposeAnotherUsersEntitlement()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var ownerClient =
            factory.CreateHttpsClient();

        using var attackerClient =
            factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterAndLoginAsync(
                ownerClient);

        var attacker =
            await TestUserAuthentication.RegisterAndLoginAsync(
                attackerClient);

        var ownerUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                owner.Email);

        await using (var scope =
                     factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<FullWorthDbContext>();

            var now =
                DateTimeOffset.UtcNow;

            dbContext.SubscriptionEntitlements.Add(
                new SubscriptionEntitlementEntity
                {
                    Id =
                        Guid.NewGuid(),
                    UserId =
                        ownerUserId,
                    Tier =
                        FullWorthSubscriptionTier.Standard,
                    Source =
                        SubscriptionEntitlementSource.Internal,
                    StartsAtUtc =
                        now.AddMinutes(
                            -5),
                    EndsAtUtc =
                        now.AddDays(
                            1),
                    IsRevoked =
                        false,
                    CreatedAtUtc =
                        now,
                    UpdatedAtUtc =
                        now
                });

            await dbContext.SaveChangesAsync();
        }

        TestUserAuthentication.Authorize(
            attackerClient,
            attacker);

        using var attackerResponse =
            await attackerClient.GetAsync(
                "/api/subscription");

        attackerResponse.EnsureSuccessStatusCode();

        var attackerStatus =
            await attackerResponse.Content
                .ReadFromJsonAsync<SubscriptionStatusPayload>();

        Assert.NotNull(
            attackerStatus);

        Assert.False(
            attackerStatus.IsActive);

        Assert.Null(
            attackerStatus.Tier);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var ownerResponse =
            await ownerClient.GetAsync(
                "/api/subscription");

        ownerResponse.EnsureSuccessStatusCode();

        var ownerStatus =
            await ownerResponse.Content
                .ReadFromJsonAsync<SubscriptionStatusPayload>();

        Assert.NotNull(
            ownerStatus);

        Assert.True(
            ownerStatus.IsActive);

        Assert.Equal(
            nameof(FullWorthSubscriptionTier.Standard),
            ownerStatus.Tier);
    }

    [Fact]
    public async Task RecentPaydayPlans_DoNotExposeAnotherUsersHistory()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var ownerClient =
            factory.CreateHttpsClient();

        using var attackerClient =
            factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterAndLoginAsync(
                ownerClient);

        var attacker =
            await TestUserAuthentication.RegisterAndLoginAsync(
                attackerClient);

        var ownerUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                owner.Email);

        var ownerPlanId =
            Guid.NewGuid();

        await using (var scope =
                     factory.Services.CreateAsyncScope())
        {
            var dbContext =
                scope.ServiceProvider
                    .GetRequiredService<FullWorthDbContext>();

            dbContext.PlanningPaycheckPlanRuns.Add(
                new PlanningPaycheckPlanRunEntity
                {
                    Id =
                        ownerPlanId,
                    UserId =
                        ownerUserId,
                    PayrollTransactionId =
                        Guid.NewGuid(),
                    PaycheckPostedDate =
                        new DateOnly(
                            2026,
                            10,
                            2),
                    PaycheckAmount =
                        1000m,
                    CurrencyCode =
                        "USD",
                    RecommendedSetAside =
                        300m,
                    PaycheckRemainingAfterPlan =
                        700m,
                    Shortfall =
                        0m,
                    CreatedAtUtc =
                        DateTimeOffset.UtcNow
                });

            await dbContext.SaveChangesAsync();
        }

        TestUserAuthentication.Authorize(
            attackerClient,
            attacker);

        using var attackerResponse =
            await attackerClient.GetAsync(
                "/api/planning/payday-plans/recent");

        attackerResponse.EnsureSuccessStatusCode();

        var attackerPlans =
            await attackerResponse.Content
                .ReadFromJsonAsync<List<PaydayPlanSummaryPayload>>();

        Assert.NotNull(
            attackerPlans);

        Assert.Empty(
            attackerPlans);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var ownerResponse =
            await ownerClient.GetAsync(
                "/api/planning/payday-plans/recent");

        ownerResponse.EnsureSuccessStatusCode();

        var ownerPlans =
            await ownerResponse.Content
                .ReadFromJsonAsync<List<PaydayPlanSummaryPayload>>();

        Assert.NotNull(
            ownerPlans);

        var ownerPlan =
            Assert.Single(
                ownerPlans);

        Assert.Equal(
            ownerPlanId,
            ownerPlan.Id);
    }

    private sealed class SubscriptionStatusPayload
    {
        public bool IsActive { get; set; }

        public string? Tier { get; set; }
    }

    private sealed class PaydayPlanSummaryPayload
    {
        public Guid Id { get; set; }
    }
}

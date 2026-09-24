using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class AdminSubscriptionReadGatewayTests
{
    private static readonly DateTimeOffset NowUtc =
        new(
            2026,
            9,
            24,
            6,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task GetUserSubscriptions_ReturnsOnlyCurrentOwnedSubscriptionState()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var emptyUserId =
            Guid.NewGuid();

        dbContext.SubscriptionEntitlements.AddRange(
            new SubscriptionEntitlementEntity
            {
                UserId =
                    userId,

                Tier =
                    FullWorthSubscriptionTier.Standard,

                Source =
                    SubscriptionEntitlementSource.Complimentary,

                StartsAtUtc =
                    NowUtc.AddDays(
                        -1),

                EndsAtUtc =
                    NowUtc.AddDays(
                        5)
            },
            new SubscriptionEntitlementEntity
            {
                UserId =
                    userId,

                Tier =
                    FullWorthSubscriptionTier.Beta,

                Source =
                    SubscriptionEntitlementSource.BetaProgram,

                StartsAtUtc =
                    NowUtc.AddDays(
                        -10),

                EndsAtUtc =
                    NowUtc.AddMinutes(
                        -1)
            },
            new SubscriptionEntitlementEntity
            {
                UserId =
                    userId,

                Tier =
                    FullWorthSubscriptionTier.Beta,

                Source =
                    SubscriptionEntitlementSource.Internal,

                StartsAtUtc =
                    NowUtc.AddDays(
                        -1),

                IsRevoked =
                    true,

                RevokedAtUtc =
                    NowUtc.AddHours(
                        -1)
            });

        dbContext.UserProgramMemberships.AddRange(
            new UserProgramMembershipEntity
            {
                UserId =
                    userId,

                Program =
                    UserProgramType.BetaTester,

                StartsAtUtc =
                    NowUtc.AddDays(
                        -1),

                IsActive =
                    true
            },
            new UserProgramMembershipEntity
            {
                UserId =
                    userId,

                Program =
                    UserProgramType.InternalTester,

                StartsAtUtc =
                    NowUtc.AddDays(
                        -10),

                EndsAtUtc =
                    NowUtc.AddMinutes(
                        -1),

                IsActive =
                    true
            });

        await dbContext.SaveChangesAsync();

        var gateway =
            new AdminSubscriptionReadGateway(
                dbContext,
                new FixedTimeProvider(
                    NowUtc));

        var results =
            await gateway.GetUserSubscriptionsAsync(
                [
                    userId,
                    emptyUserId
                ]);

        Assert.Equal(
            2,
            results.Count);

        var active =
            results[userId];

        Assert.Equal(
            ["BetaTester"],
            active.Programs);

        Assert.Equal(
            "Standard",
            active.SubscriptionTier);

        Assert.Equal(
            NowUtc.AddDays(
                5),
            active.SubscriptionEndsAtUtc);

        var empty =
            results[emptyUserId];

        Assert.Empty(
            empty.Programs);

        Assert.Null(
            empty.SubscriptionTier);

        Assert.Null(
            empty.SubscriptionEndsAtUtc);
    }

    [Fact]
    public async Task GetUserSubscriptions_EmptyInputReturnsEmptyWithoutQueryResults()
    {
        await using var dbContext =
            CreateDbContext();

        var gateway =
            new AdminSubscriptionReadGateway(
                dbContext,
                new FixedTimeProvider(
                    NowUtc));

        var results =
            await gateway.GetUserSubscriptionsAsync(
                []);

        Assert.Empty(
            results);
    }

    private static FullWorthDbContext
        CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<
                    FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"admin-subscription-read-{Guid.NewGuid():N}")
                .Options);
    }

    private sealed class FixedTimeProvider(
        DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return value;
        }
    }
}

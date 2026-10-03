using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class AccountSubscriptionDeletionGatewayTests
{
    [Fact]
    public async Task ApplyOwnedDataDeletionAsync_PreservesOtherUsersSubscriptionState()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"subscription-deletion-{Guid.NewGuid():N}")
                .Options;

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var deletingUserId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var creatorUserId =
            Guid.NewGuid();

        var deletingEntitlement =
            CreateEntitlement(
                deletingUserId);

        var otherEntitlement =
            CreateEntitlement(
                otherUserId);

        var deletingMembership =
            CreateMembership(
                deletingUserId,
                creatorUserId);

        var otherMembership =
            CreateMembership(
                otherUserId,
                creatorUserId);

        var deletingKey =
            CreateAccessKey(
                creatorUserId,
                "DELETE");

        var otherKey =
            CreateAccessKey(
                creatorUserId,
                "OTHER");

        dbContext.SubscriptionEntitlements.AddRange(
            deletingEntitlement,
            otherEntitlement);

        dbContext.UserProgramMemberships.AddRange(
            deletingMembership,
            otherMembership);

        dbContext.SubscriptionAccessKeys.AddRange(
            deletingKey,
            otherKey);

        await dbContext.SaveChangesAsync();

        var deletingRedemption =
            new SubscriptionAccessKeyRedemptionEntity
            {
                AccessKeyId =
                    deletingKey.Id,
                UserId =
                    deletingUserId,
                EntitlementId =
                    deletingEntitlement.Id
            };

        var otherRedemption =
            new SubscriptionAccessKeyRedemptionEntity
            {
                AccessKeyId =
                    otherKey.Id,
                UserId =
                    otherUserId,
                EntitlementId =
                    otherEntitlement.Id
            };

        dbContext.SubscriptionAccessKeyRedemptions.AddRange(
            deletingRedemption,
            otherRedemption);

        await dbContext.SaveChangesAsync();

        var gateway =
            new AccountSubscriptionDeletionGateway(
                dbContext);

        await gateway.ApplyOwnedDataDeletionAsync(
            deletingUserId);

        Assert.False(
            await dbContext.SubscriptionEntitlements.AnyAsync(
                entitlement =>
                    entitlement.UserId ==
                    deletingUserId));

        Assert.False(
            await dbContext.UserProgramMemberships.AnyAsync(
                membership =>
                    membership.UserId ==
                    deletingUserId));

        Assert.False(
            await dbContext.SubscriptionAccessKeyRedemptions.AnyAsync(
                redemption =>
                    redemption.UserId ==
                    deletingUserId));

        Assert.True(
            await dbContext.SubscriptionEntitlements.AnyAsync(
                entitlement =>
                    entitlement.Id ==
                    otherEntitlement.Id &&
                    entitlement.UserId ==
                    otherUserId));

        Assert.True(
            await dbContext.UserProgramMemberships.AnyAsync(
                membership =>
                    membership.Id ==
                    otherMembership.Id &&
                    membership.UserId ==
                    otherUserId));

        Assert.True(
            await dbContext.SubscriptionAccessKeyRedemptions.AnyAsync(
                redemption =>
                    redemption.Id ==
                    otherRedemption.Id &&
                    redemption.UserId ==
                    otherUserId));

        Assert.Equal(
            2,
            await dbContext.SubscriptionAccessKeys.CountAsync());
    }

    private static SubscriptionEntitlementEntity CreateEntitlement(
        Guid userId)
    {
        var now =
            DateTimeOffset.UtcNow;

        return new SubscriptionEntitlementEntity
        {
            Id =
                Guid.NewGuid(),
            UserId =
                userId,
            Tier =
                FullWorthSubscriptionTier.Beta,
            Source =
                SubscriptionEntitlementSource.AccessKey,
            StartsAtUtc =
                now.AddMinutes(
                    -1),
            EndsAtUtc =
                now.AddDays(
                    30),
            CreatedAtUtc =
                now,
            UpdatedAtUtc =
                now
        };
    }

    private static UserProgramMembershipEntity CreateMembership(
        Guid userId,
        Guid grantedByUserId)
    {
        var now =
            DateTimeOffset.UtcNow;

        return new UserProgramMembershipEntity
        {
            Id =
                Guid.NewGuid(),
            UserId =
                userId,
            Program =
                UserProgramType.BetaTester,
            StartsAtUtc =
                now.AddMinutes(
                    -1),
            IsActive =
                true,
            GrantedByUserId =
                grantedByUserId,
            CreatedAtUtc =
                now,
            UpdatedAtUtc =
                now
        };
    }

    private static SubscriptionAccessKeyEntity CreateAccessKey(
        Guid createdByUserId,
        string label)
    {
        return new SubscriptionAccessKeyEntity
        {
            Id =
                Guid.NewGuid(),
            KeyHash =
                Convert.ToHexString(
                    Guid.NewGuid().ToByteArray()),
            DisplayPrefix =
                $"FW-{label}",
            Label =
                label,
            Purpose =
                SubscriptionAccessKeyPurpose.Beta,
            Tier =
                FullWorthSubscriptionTier.Beta,
            DurationDays =
                30,
            MaxRedemptions =
                1,
            RedemptionCount =
                1,
            CreatedByUserId =
                createdByUserId
        };
    }
}

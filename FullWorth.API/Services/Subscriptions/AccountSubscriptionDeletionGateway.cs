using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Subscriptions;

public sealed class AccountSubscriptionDeletionGateway(
    FullWorthDbContext dbContext)
    : IAccountSubscriptionDeletionGateway
{
    public async Task ApplyOwnedDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        /*
         * Redemptions restrict entitlement deletion and must be removed
         * first. Access-key RedemptionCount intentionally remains consumed.
         */
        var redemptions =
            await dbContext.SubscriptionAccessKeyRedemptions
                .Where(
                    redemption =>
                        redemption.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.SubscriptionAccessKeyRedemptions
            .RemoveRange(
                redemptions);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        var entitlements =
            await dbContext.SubscriptionEntitlements
                .Where(
                    entitlement =>
                        entitlement.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var memberships =
            await dbContext.UserProgramMemberships
                .Where(
                    membership =>
                        membership.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.SubscriptionEntitlements
            .RemoveRange(
                entitlements);

        dbContext.UserProgramMemberships
            .RemoveRange(
                memberships);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Subscriptions;

public sealed class AdminSubscriptionReadGateway(
    FullWorthDbContext dbContext,
    TimeProvider timeProvider)
    : IAdminSubscriptionReadGateway
{
    public async Task<AdminAccessKeyPageReadSnapshot>
        GetAccessKeysAsync(
            int skip,
            int take,
            CancellationToken cancellationToken = default)
    {
        if (skip <
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(skip));
        }

        if (take is <
                1 or >
                100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take));
        }

        var query =
            dbContext.SubscriptionAccessKeys
                .AsNoTracking();

        var totalCount =
            await query.CountAsync(
                cancellationToken);

        var rows =
            await query
                .OrderByDescending(
                    item =>
                        item.CreatedAtUtc)
                .ThenByDescending(
                    item =>
                        item.Id)
                .Select(
                    item =>
                        new
                        {
                            item.Id,
                            item.DisplayPrefix,
                            item.Label,
                            item.Purpose,
                            item.Tier,
                            item.DurationDays,
                            item.GrantsLifetimeAccess,
                            item.MaxRedemptions,
                            item.RedemptionCount,
                            item.ExpiresAtUtc,
                            item.IsRevoked,
                            item.RevokedAtUtc,
                            item.CreatedByUserId,
                            item.CreatedAtUtc
                        })
                .Skip(
                    skip)
                .Take(
                    take)
                .ToListAsync(
                    cancellationToken);

        var nowUtc =
            timeProvider.GetUtcNow();

        var items =
            rows
                .Select(
                    item =>
                        new AdminAccessKeyReadRecord(
                            item.Id,
                            item.DisplayPrefix,
                            item.Label,
                            item.Purpose.ToString(),
                            item.Tier.ToString(),
                            item.DurationDays,
                            item.GrantsLifetimeAccess,
                            item.MaxRedemptions,
                            item.RedemptionCount,
                            item.ExpiresAtUtc,
                            GetAccessKeyStatus(
                                item.IsRevoked,
                                item.ExpiresAtUtc,
                                item.RedemptionCount,
                                item.MaxRedemptions,
                                nowUtc),
                            item.RevokedAtUtc,
                            item.CreatedByUserId,
                            item.CreatedAtUtc))
                .ToArray();

        return new AdminAccessKeyPageReadSnapshot(
            skip,
            take,
            totalCount,
            items);
    }

    public async Task<IReadOnlyDictionary<
            Guid,
            AdminUserSubscriptionReadRecord>>
        GetUserSubscriptionsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            userIds);

        if (userIds.Count ==
            0)
        {
            return new Dictionary<
                Guid,
                AdminUserSubscriptionReadRecord>();
        }

        var ids =
            new HashSet<Guid>();

        foreach (var userId in
                 userIds)
        {
            if (userId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "User IDs must not be empty.",
                    nameof(userIds));
            }

            ids.Add(
                userId);
        }

        var requestedIds =
            ids.ToArray();

        var nowUtc =
            timeProvider.GetUtcNow();

        var entitlements =
            await dbContext.SubscriptionEntitlements
                .AsNoTracking()
                .Where(
                    item =>
                        requestedIds.Contains(
                            item.UserId) &&
                        !item.IsRevoked &&
                        item.StartsAtUtc <=
                            nowUtc &&
                        (
                            item.EndsAtUtc ==
                                null ||
                            item.EndsAtUtc >
                                nowUtc
                        ))
                .ToListAsync(
                    cancellationToken);

        var memberships =
            await dbContext.UserProgramMemberships
                .AsNoTracking()
                .Where(
                    item =>
                        requestedIds.Contains(
                            item.UserId) &&
                        item.IsActive &&
                        (
                            item.EndsAtUtc ==
                                null ||
                            item.EndsAtUtc >
                                nowUtc
                        ))
                .ToListAsync(
                    cancellationToken);

        var results =
            new Dictionary<
                Guid,
                AdminUserSubscriptionReadRecord>(
                    requestedIds.Length);

        foreach (var userId in
                 requestedIds)
        {
            var effective =
                SubscriptionEntitlementRules
                    .SelectEffectiveEntitlement(
                        entitlements.Where(
                            item =>
                                item.UserId ==
                                    userId),
                        nowUtc);

            var programs =
                memberships
                    .Where(
                        item =>
                            item.UserId ==
                                userId)
                    .Select(
                        item =>
                            item.Program.ToString())
                    .OrderBy(
                        name =>
                            name,
                        StringComparer.Ordinal)
                    .ToArray();

            results.Add(
                userId,
                new AdminUserSubscriptionReadRecord(
                    userId,
                    programs,
                    effective?.Tier.ToString(),
                    effective?.EndsAtUtc));
        }

        return results;
    }

    private static string GetAccessKeyStatus(
        bool isRevoked,
        DateTimeOffset? expiresAtUtc,
        int redemptionCount,
        int maxRedemptions,
        DateTimeOffset nowUtc)
    {
        if (isRevoked)
        {
            return "Revoked";
        }

        if (expiresAtUtc.HasValue &&
            expiresAtUtc.Value <=
                nowUtc)
        {
            return "Expired";
        }

        if (redemptionCount >=
            maxRedemptions)
        {
            return "Exhausted";
        }

        return "Active";
    }
}

namespace FullWorth.API.Services.Contracts;

public sealed record AdminAccessKeyReadRecord(
    Guid Id,
    string DisplayPrefix,
    string? Label,
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    int RedemptionCount,
    DateTimeOffset? ExpiresAtUtc,
    string Status,
    DateTimeOffset? RevokedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public sealed record AdminAccessKeyPageReadSnapshot(
    int Skip,
    int Take,
    int TotalCount,
    IReadOnlyList<AdminAccessKeyReadRecord> Items);

public sealed record AdminUserSubscriptionReadRecord(
    Guid UserId,
    IReadOnlyList<string> Programs,
    string? SubscriptionTier,
    DateTimeOffset? SubscriptionEndsAtUtc);

public interface IAdminSubscriptionReadGateway
{
    Task<AdminAccessKeyPageReadSnapshot> GetAccessKeysAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, AdminUserSubscriptionReadRecord>>
        GetUserSubscriptionsAsync(
            IReadOnlyCollection<Guid> userIds,
            CancellationToken cancellationToken = default);
}

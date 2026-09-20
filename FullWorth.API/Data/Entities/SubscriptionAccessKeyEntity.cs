using System.ComponentModel.DataAnnotations;
using FullWorth.API.Data.Configurations;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Data.Entities;

public enum SubscriptionAccessKeyPurpose
{
    Complimentary,
    Beta
}

[EntityTypeConfiguration(
    typeof(SubscriptionAccessKeyEntityConfiguration))]
public sealed class SubscriptionAccessKeyEntity
{
    public Guid Id { get; set; } =
        Guid.NewGuid();

    public string KeyHash { get; set; } =
        string.Empty;

    public string DisplayPrefix { get; set; } =
        string.Empty;

    [MaxLength(120)]
    public string? Label { get; set; }

    public SubscriptionAccessKeyPurpose Purpose { get; set; } =
        SubscriptionAccessKeyPurpose.Complimentary;

    public FullWorthSubscriptionTier Tier { get; set; } =
        FullWorthSubscriptionTier.Standard;

    public int? DurationDays { get; set; }

    public bool GrantsLifetimeAccess { get; set; }

    public int MaxRedemptions { get; set; } =
        1;

    public int RedemptionCount { get; set; }

    public DateTimeOffset? ExpiresAtUtc { get; set; }

    public bool IsRevoked { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public Guid CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public ApplicationUser CreatedByUser { get; set; } =
        null!;

    public ICollection<SubscriptionAccessKeyRedemptionEntity> Redemptions
    {
        get;
        set;
    } = [];
}

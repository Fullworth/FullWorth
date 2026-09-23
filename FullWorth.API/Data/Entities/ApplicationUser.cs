using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace FullWorth.API.Data.Entities;

public enum TimestampDisplayMode
{
    Local12Hour = 0,
    Utc = 1
}

public enum UiThemePreference
{
    System = 0,
    Light = 1,
    Dark = 2
}

public enum UiTextSizePreference
{
    Standard = 0,
    Large = 1,
    ExtraLarge = 2
}

[Flags]
public enum ExperienceFocus
{
    None = 0,
    BillChanges = 1 << 0,
    Spending = 1 << 1,
    RecurringCosts = 1 << 2,
    AccountOverview = 1 << 3,
    Statements = 1 << 4
}

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public const string DisplayNameClaimType =
        "billwatch:display_name";

    public const int MaxDisplayNameLength = 80;

    public const int MaxPreferredUiLanguageLength = 10;

    public DateTimeOffset CreatedAtUtc { get; set; } =
        DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    public bool IsActive { get; set; } = true;

    public TimestampDisplayMode TimestampDisplayMode { get; set; } =
        TimestampDisplayMode.Local12Hour;

    public DateTimeOffset? ExperienceSetupCompletedAtUtc { get; set; }

    [MaxLength(MaxPreferredUiLanguageLength)]
    public string PreferredUiLanguage { get; set; } =
        "en-US";

    public UiThemePreference ThemePreference { get; set; } =
        UiThemePreference.System;

    public UiTextSizePreference TextSizePreference { get; set; } =
        UiTextSizePreference.Standard;

    public bool HighContrastEnabled { get; set; }

    public bool ReduceMotionEnabled { get; set; }

    public ExperienceFocus ExperienceFocus { get; set; } =
        ExperienceFocus.BillChanges |
        ExperienceFocus.AccountOverview;

    public ICollection<SubscriptionEntitlementEntity> SubscriptionEntitlements
    {
        get;
        set;
    } = [];

    public ICollection<SubscriptionEntitlementEntity> GrantedSubscriptionEntitlements
    {
        get;
        set;
    } = [];

    public ICollection<UserProgramMembershipEntity> ProgramMemberships
    {
        get;
        set;
    } = [];

    public ICollection<UserProgramMembershipEntity> GrantedProgramMemberships
    {
        get;
        set;
    } = [];

    public ICollection<SubscriptionAccessKeyEntity> CreatedSubscriptionAccessKeys
    {
        get;
        set;
    } = [];

    public ICollection<SubscriptionAccessKeyRedemptionEntity> SubscriptionAccessKeyRedemptions
    {
        get;
        set;
    } = [];

    public ICollection<AdminAuditLogEntity> AdminAuditActions
    {
        get;
        set;
    } = [];

    public ICollection<AdminAuditLogEntity> AdminAuditTargets
    {
        get;
        set;
    } = [];
}

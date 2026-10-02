namespace FullWorth.API.Infrastructure;

internal static class SecuritySensitiveActionAlertNames
{
    internal const string RepeatedAdminMutations =
        "repeated_admin_mutations";

    internal const string RepeatedAccountExports =
        "repeated_account_exports";

    internal const string RepeatedAccountDeletions =
        "repeated_account_deletions";

    internal const string RepeatedFinancialProviderAttention =
        "repeated_financial_provider_attention";
}

internal static class SecuritySensitiveActionAlertIds
{
    internal static readonly EventId RepeatedAdminMutations =
        new(
            29111,
            nameof(RepeatedAdminMutations));

    internal static readonly EventId RepeatedAccountExports =
        new(
            29112,
            nameof(RepeatedAccountExports));

    internal static readonly EventId RepeatedAccountDeletions =
        new(
            29113,
            nameof(RepeatedAccountDeletions));

    internal static readonly EventId RepeatedFinancialProviderAttention =
        new(
            29114,
            nameof(RepeatedFinancialProviderAttention));
}

internal sealed record SecuritySensitiveActionObservation(
    string Name,
    string DimensionName,
    string DimensionValue);

internal sealed record SecuritySensitiveActionAlert(
    string Name,
    string SecurityEventName,
    string DimensionName,
    string DimensionValue,
    int Threshold,
    int WindowSeconds,
    int SuppressedThresholds);

internal interface ISecuritySensitiveActionAlertSink
{
    void Write(
        SecuritySensitiveActionAlert securityAlert);
}

internal sealed class LoggerSecuritySensitiveActionAlertSink(
    ILoggerFactory loggerFactory)
    : ISecuritySensitiveActionAlertSink
{
    private readonly ILogger _logger =
        loggerFactory.CreateLogger(
            "FullWorth.SecurityAlerts");

    public void Write(
        SecuritySensitiveActionAlert securityAlert)
    {
        ArgumentNullException.ThrowIfNull(
            securityAlert);

        var eventId =
            securityAlert.Name switch
            {
                SecuritySensitiveActionAlertNames
                    .RepeatedAdminMutations =>
                    SecuritySensitiveActionAlertIds
                        .RepeatedAdminMutations,

                SecuritySensitiveActionAlertNames
                    .RepeatedAccountExports =>
                    SecuritySensitiveActionAlertIds
                        .RepeatedAccountExports,

                SecuritySensitiveActionAlertNames
                    .RepeatedAccountDeletions =>
                    SecuritySensitiveActionAlertIds
                        .RepeatedAccountDeletions,

                SecuritySensitiveActionAlertNames
                    .RepeatedFinancialProviderAttention =>
                    SecuritySensitiveActionAlertIds
                        .RepeatedFinancialProviderAttention,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(securityAlert),
                        "Unknown sensitive-action security alert.")
            };

        _logger.LogWarning(
            eventId,
            "Security alert {SecurityAlertName} event={SecurityEventName} dimension={SecurityDimensionName} value={SecurityDimensionValue} threshold={Threshold} window_seconds={WindowSeconds} suppressed_thresholds={SuppressedThresholds}",
            securityAlert.Name,
            securityAlert.SecurityEventName,
            securityAlert.DimensionName,
            securityAlert.DimensionValue,
            securityAlert.Threshold,
            securityAlert.WindowSeconds,
            securityAlert.SuppressedThresholds);
    }
}

public sealed class SecuritySensitiveActionAlertAggregator
{
    internal static readonly TimeSpan Window =
        TimeSpan.FromMinutes(
            5);

    internal static readonly TimeSpan AlertCooldown =
        TimeSpan.FromMinutes(
            15);

    private readonly object _gate =
        new();

    private readonly Dictionary<
        SecuritySensitiveActionAggregationKey,
        SecuritySensitiveActionAggregationBucket> _buckets =
            [];

    private readonly TimeProvider _timeProvider;

    private readonly ISecuritySensitiveActionAlertSink _alertSink;

    internal SecuritySensitiveActionAlertAggregator(
        TimeProvider timeProvider,
        ISecuritySensitiveActionAlertSink alertSink)
    {
        ArgumentNullException.ThrowIfNull(
            timeProvider);

        ArgumentNullException.ThrowIfNull(
            alertSink);

        _timeProvider =
            timeProvider;

        _alertSink =
            alertSink;
    }

    internal int BucketCount
    {
        get
        {
            lock (_gate)
            {
                return _buckets.Count;
            }
        }
    }

    internal void Observe(
        SecuritySensitiveActionObservation securityEvent)
    {
        ArgumentNullException.ThrowIfNull(
            securityEvent);

        SecuritySensitiveActionAlert? securityAlert;

        lock (_gate)
        {
            var key =
                ValidateAndCreateKey(
                    securityEvent);

            var now =
                _timeProvider.GetUtcNow();

            if (!_buckets.TryGetValue(
                    key,
                    out var bucket))
            {
                bucket =
                    new SecuritySensitiveActionAggregationBucket(
                        now);

                _buckets.Add(
                    key,
                    bucket);
            }

            if (now -
                    bucket.WindowStartedAt >=
                Window)
            {
                bucket.WindowStartedAt =
                    now;

                bucket.Count =
                    0;
            }

            bucket.Count++;

            var threshold =
                GetThreshold(
                    key.Name);

            if (bucket.Count <
                threshold)
            {
                return;
            }

            bucket.Count =
                0;

            bucket.WindowStartedAt =
                now;

            if (now <
                bucket.NextAlertAllowedAt)
            {
                bucket.SuppressedThresholds++;

                return;
            }

            securityAlert =
                CreateAlert(
                    key,
                    threshold,
                    bucket.SuppressedThresholds);

            bucket.SuppressedThresholds =
                0;

            bucket.NextAlertAllowedAt =
                now +
                AlertCooldown;
        }

        _alertSink.Write(
            securityAlert);
    }

    private static SecuritySensitiveActionAggregationKey
        ValidateAndCreateKey(
            SecuritySensitiveActionObservation securityEvent)
    {
        var valid =
            securityEvent switch
            {
                {
                    Name:
                        SecurityEventNames
                            .AdminMutationCompleted,
                    DimensionName:
                        "action",
                    DimensionValue:
                        "StaffRoleAssigned"
                        or "StaffRoleRemoved"
                        or "SubscriptionEntitlementGranted"
                        or "SubscriptionEntitlementRevoked"
                        or "UserProgramMembershipEnabled"
                        or "UserProgramMembershipDisabled"
                        or "SubscriptionAccessKeyCreated"
                        or "SubscriptionAccessKeyRevoked"
                } =>
                    true,

                {
                    Name:
                        SecurityEventNames
                            .AccountExportCompleted,
                    DimensionName:
                        "scope",
                    DimensionValue:
                        "application"
                } =>
                    true,

                {
                    Name:
                        SecurityEventNames
                            .AccountDeletionCompleted,
                    DimensionName:
                        "scope",
                    DimensionValue:
                        "application"
                } =>
                    true,

                {
                    Name:
                        SecurityEventNames
                            .FinancialProviderAttentionRequired,
                    DimensionName:
                        "operation",
                    DimensionValue:
                        "accounts_sync"
                        or "transactions_sync"
                } =>
                    true,

                _ =>
                    false
            };

        if (!valid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(securityEvent),
                "Unknown sensitive-action security event or dimension.");
        }

        return new SecuritySensitiveActionAggregationKey(
            securityEvent.Name,
            securityEvent.DimensionName,
            securityEvent.DimensionValue);
    }

    private static int GetThreshold(
        string securityEventName)
    {
        return securityEventName switch
        {
            SecurityEventNames
                .AdminMutationCompleted =>
                5,

            SecurityEventNames
                .AccountExportCompleted =>
                10,

            SecurityEventNames
                .AccountDeletionCompleted =>
                3,

            SecurityEventNames
                .FinancialProviderAttentionRequired =>
                5,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(securityEventName),
                    "Unknown sensitive-action security event.")
        };
    }

    private static SecuritySensitiveActionAlert CreateAlert(
        SecuritySensitiveActionAggregationKey key,
        int threshold,
        int suppressedThresholds)
    {
        var alertName =
            key.Name switch
            {
                SecurityEventNames
                    .AdminMutationCompleted =>
                    SecuritySensitiveActionAlertNames
                        .RepeatedAdminMutations,

                SecurityEventNames
                    .AccountExportCompleted =>
                    SecuritySensitiveActionAlertNames
                        .RepeatedAccountExports,

                SecurityEventNames
                    .AccountDeletionCompleted =>
                    SecuritySensitiveActionAlertNames
                        .RepeatedAccountDeletions,

                SecurityEventNames
                    .FinancialProviderAttentionRequired =>
                    SecuritySensitiveActionAlertNames
                        .RepeatedFinancialProviderAttention,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(key),
                        "Unknown sensitive-action security event.")
            };

        return new SecuritySensitiveActionAlert(
            alertName,
            key.Name,
            key.DimensionName,
            key.DimensionValue,
            threshold,
            checked(
                (int)Window.TotalSeconds),
            suppressedThresholds);
    }

    private readonly record struct
        SecuritySensitiveActionAggregationKey(
            string Name,
            string DimensionName,
            string DimensionValue);

    private sealed class
        SecuritySensitiveActionAggregationBucket(
            DateTimeOffset now)
    {
        internal int Count { get; set; }

        internal DateTimeOffset WindowStartedAt { get; set; } =
            now;

        internal DateTimeOffset NextAlertAllowedAt { get; set; } =
            DateTimeOffset.MinValue;

        internal int SuppressedThresholds { get; set; }
    }
}

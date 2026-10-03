namespace FullWorth.API.Infrastructure;

internal static class SecurityEventAlertNames
{
    internal const string RepeatedAuthenticationRejections =
        "repeated_authentication_rejections";

    internal const string RepeatedAuthorizationDenials =
        "repeated_authorization_denials";

    internal const string RepeatedRateLimitRejections =
        "repeated_rate_limit_rejections";

    internal const string RepeatedOwnershipScopedResourceMisses =
        "repeated_ownership_scoped_resource_misses";

    internal const string AggregationCapacityReached =
        "security_event_aggregation_capacity_reached";
}

internal static class SecurityEventAlertIds
{
    internal static readonly EventId RepeatedAuthenticationRejections =
        new(
            29101,
            nameof(RepeatedAuthenticationRejections));

    internal static readonly EventId RepeatedAuthorizationDenials =
        new(
            29102,
            nameof(RepeatedAuthorizationDenials));

    internal static readonly EventId RepeatedRateLimitRejections =
        new(
            29103,
            nameof(RepeatedRateLimitRejections));

    internal static readonly EventId RepeatedOwnershipScopedResourceMisses =
        new(
            29105,
            nameof(RepeatedOwnershipScopedResourceMisses));

    internal static readonly EventId AggregationCapacityReached =
        new(
            29104,
            nameof(AggregationCapacityReached));
}

internal sealed record SecurityEventAlert(
    string Name,
    string SecurityEventName,
    string HttpMethod,
    string EndpointPattern,
    bool Authenticated,
    int Threshold,
    int WindowSeconds,
    int SuppressedThresholds);

internal interface ISecurityEventAlertSink
{
    void Write(SecurityEventAlert securityAlert);
}

internal sealed class LoggerSecurityEventAlertSink(
    ILoggerFactory loggerFactory)
    : ISecurityEventAlertSink
{
    private readonly ILogger _logger =
        loggerFactory.CreateLogger(
            "FullWorth.SecurityAlerts");

    public void Write(
        SecurityEventAlert securityAlert)
    {
        ArgumentNullException.ThrowIfNull(
            securityAlert);

        var eventId =
            securityAlert.Name switch
            {
                SecurityEventAlertNames
                    .RepeatedAuthenticationRejections =>
                    SecurityEventAlertIds
                        .RepeatedAuthenticationRejections,

                SecurityEventAlertNames
                    .RepeatedAuthorizationDenials =>
                    SecurityEventAlertIds
                        .RepeatedAuthorizationDenials,

                SecurityEventAlertNames
                    .RepeatedRateLimitRejections =>
                    SecurityEventAlertIds
                        .RepeatedRateLimitRejections,

                SecurityEventAlertNames
                    .RepeatedOwnershipScopedResourceMisses =>
                    SecurityEventAlertIds
                        .RepeatedOwnershipScopedResourceMisses,

                SecurityEventAlertNames
                    .AggregationCapacityReached =>
                    SecurityEventAlertIds
                        .AggregationCapacityReached,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(securityAlert),
                        "Unknown security alert.")
            };

        _logger.LogWarning(
            eventId,
            "Security alert {SecurityAlertName} event={SecurityEventName} method={HttpMethod} endpoint={EndpointPattern} authenticated={Authenticated} threshold={Threshold} window_seconds={WindowSeconds} suppressed_thresholds={SuppressedThresholds}",
            securityAlert.Name,
            securityAlert.SecurityEventName,
            securityAlert.HttpMethod,
            securityAlert.EndpointPattern,
            securityAlert.Authenticated,
            securityAlert.Threshold,
            securityAlert.WindowSeconds,
            securityAlert.SuppressedThresholds);
    }
}

internal sealed class SecurityEventAlertAggregator
{
    internal const int MaximumBucketCount =
        512;

    internal static readonly TimeSpan Window =
        TimeSpan.FromMinutes(
            5);

    internal static readonly TimeSpan AlertCooldown =
        TimeSpan.FromMinutes(
            15);

    private static readonly SecurityEventAggregationKey
        OverflowKey =
            new(
                "aggregation_capacity",
                "OTHER",
                "<aggregate-overflow>",
                false);

    private static readonly TimeSpan BucketRetention =
        Window +
        AlertCooldown;

    private readonly object _gate =
        new();

    private readonly Dictionary<
        SecurityEventAggregationKey,
        SecurityEventAggregationBucket> _buckets =
            [];

    private readonly TimeProvider _timeProvider;

    private readonly ISecurityEventAlertSink _alertSink;

    internal SecurityEventAlertAggregator(
        TimeProvider timeProvider,
        ISecurityEventAlertSink alertSink)
    {
        ArgumentNullException.ThrowIfNull(
            timeProvider);

        ArgumentNullException.ThrowIfNull(
            alertSink);

        _timeProvider =
            timeProvider;

        _alertSink =
            alertSink;

        var now =
            _timeProvider.GetUtcNow();

        _buckets.Add(
            OverflowKey,
            new SecurityEventAggregationBucket(
                now));
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
        SecurityEventObservation securityEvent)
    {
        ArgumentNullException.ThrowIfNull(
            securityEvent);

        SecurityEventAlert? securityAlert;

        lock (_gate)
        {
            var now =
                _timeProvider.GetUtcNow();

            var key =
                new SecurityEventAggregationKey(
                    securityEvent.Name,
                    securityEvent.HttpMethod,
                    securityEvent.EndpointPattern,
                    securityEvent.Authenticated);

            if (!_buckets.TryGetValue(
                    key,
                    out var bucket))
            {
                PruneExpiredBuckets(
                    now);

                if (_buckets.Count >=
                    MaximumBucketCount)
                {
                    key =
                        OverflowKey;

                    bucket =
                        _buckets[
                            OverflowKey];
                }
                else
                {
                    bucket =
                        new SecurityEventAggregationBucket(
                            now);

                    _buckets.Add(
                        key,
                        bucket);
                }
            }

            bucket.LastSeenAt =
                now;

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

    private void PruneExpiredBuckets(
        DateTimeOffset now)
    {
        var staleKeys =
            _buckets
                .Where(
                    pair =>
                        pair.Key != OverflowKey &&
                        now -
                            pair.Value.LastSeenAt >=
                        BucketRetention)
                .Select(
                    pair =>
                        pair.Key)
                .ToArray();

        foreach (var staleKey in
                 staleKeys)
        {
            _buckets.Remove(
                staleKey);
        }
    }

    private static int GetThreshold(
        string securityEventName)
    {
        return securityEventName switch
        {
            SecurityEventNames
                .AuthenticationRejected =>
                25,

            SecurityEventNames
                .AuthorizationDenied =>
                10,

            SecurityEventNames
                .RateLimitRejected =>
                5,

            SecurityEventNames
                .OwnershipScopedResourceNotFound =>
                20,

            "aggregation_capacity" =>
                5,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(securityEventName),
                    "Unknown security event.")
        };
    }

    private static SecurityEventAlert CreateAlert(
        SecurityEventAggregationKey key,
        int threshold,
        int suppressedThresholds)
    {
        var alertName =
            key.Name switch
            {
                SecurityEventNames
                    .AuthenticationRejected =>
                    SecurityEventAlertNames
                        .RepeatedAuthenticationRejections,

                SecurityEventNames
                    .AuthorizationDenied =>
                    SecurityEventAlertNames
                        .RepeatedAuthorizationDenials,

                SecurityEventNames
                    .RateLimitRejected =>
                    SecurityEventAlertNames
                        .RepeatedRateLimitRejections,

                SecurityEventNames
                    .OwnershipScopedResourceNotFound =>
                    SecurityEventAlertNames
                        .RepeatedOwnershipScopedResourceMisses,

                "aggregation_capacity" =>
                    SecurityEventAlertNames
                        .AggregationCapacityReached,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(key),
                        "Unknown security event.")
            };

        return new SecurityEventAlert(
            alertName,
            key.Name,
            key.HttpMethod,
            key.EndpointPattern,
            key.Authenticated,
            threshold,
            checked(
                (int)Window.TotalSeconds),
            suppressedThresholds);
    }

    private readonly record struct SecurityEventAggregationKey(
        string Name,
        string HttpMethod,
        string EndpointPattern,
        bool Authenticated);

    private sealed class SecurityEventAggregationBucket(
        DateTimeOffset now)
    {
        internal int Count { get; set; }

        internal DateTimeOffset WindowStartedAt { get; set; } =
            now;

        internal DateTimeOffset LastSeenAt { get; set; } =
            now;

        internal DateTimeOffset NextAlertAllowedAt { get; set; } =
            DateTimeOffset.MinValue;

        internal int SuppressedThresholds { get; set; }
    }
}

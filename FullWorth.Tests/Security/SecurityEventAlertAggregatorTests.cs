using FullWorth.API.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class SecurityEventAlertAggregatorTests
{
    [Theory]
    [InlineData(
        SecurityEventNames.AuthenticationRejected,
        25,
        SecurityEventAlertNames.RepeatedAuthenticationRejections)]
    [InlineData(
        SecurityEventNames.AuthorizationDenied,
        10,
        SecurityEventAlertNames.RepeatedAuthorizationDenials)]
    [InlineData(
        SecurityEventNames.RateLimitRejected,
        5,
        SecurityEventAlertNames.RepeatedRateLimitRejections)]
    public void Thresholds_EmitOneBoundedAlert(
        string securityEventName,
        int threshold,
        string expectedAlertName)
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecurityEventAlertSink();

        var aggregator =
            new SecurityEventAlertAggregator(
                clock,
                sink);

        var observation =
            CreateObservation(
                securityEventName);

        for (var index = 0;
             index < threshold - 1;
             index++)
        {
            aggregator.Observe(
                observation);
        }

        Assert.Empty(
            sink.Alerts);

        aggregator.Observe(
            observation);

        var alert =
            Assert.Single(
                sink.Alerts);

        Assert.Equal(
            expectedAlertName,
            alert.Name);

        Assert.Equal(
            securityEventName,
            alert.SecurityEventName);

        Assert.Equal(
            threshold,
            alert.Threshold);

        Assert.Equal(
            300,
            alert.WindowSeconds);

        Assert.Equal(
            0,
            alert.SuppressedThresholds);
    }

    [Fact]
    public void Cooldown_SuppressesRepeatedThresholdsAndReportsThemLater()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecurityEventAlertSink();

        var aggregator =
            new SecurityEventAlertAggregator(
                clock,
                sink);

        var observation =
            CreateObservation(
                SecurityEventNames.RateLimitRejected);

        Observe(
            aggregator,
            observation,
            count:
                5);

        Observe(
            aggregator,
            observation,
            count:
                5);

        Assert.Single(
            sink.Alerts);

        clock.Advance(
            SecurityEventAlertAggregator
                .AlertCooldown);

        Observe(
            aggregator,
            observation,
            count:
                5);

        Assert.Equal(
            2,
            sink.Alerts.Count);

        Assert.Equal(
            1,
            sink.Alerts[1]
                .SuppressedThresholds);
    }

    [Fact]
    public void Aggregation_SeparatesSafeRouteAndAuthenticationDimensions()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecurityEventAlertSink();

        var aggregator =
            new SecurityEventAlertAggregator(
                clock,
                sink);

        for (var index = 0;
             index < 9;
             index++)
        {
            aggregator.Observe(
                CreateObservation(
                    SecurityEventNames.AuthorizationDenied));
        }

        for (var index = 0;
             index < 9;
             index++)
        {
            aggregator.Observe(
                CreateObservation(
                    SecurityEventNames.AuthorizationDenied)
                    with
                    {
                        EndpointPattern =
                            "/api/statements/{statementId:guid}"
                    });
        }

        Assert.Empty(
            sink.Alerts);

        aggregator.Observe(
            CreateObservation(
                SecurityEventNames.AuthorizationDenied));

        var alert =
            Assert.Single(
                sink.Alerts);

        Assert.Equal(
            "/api/bill-streams/{billStreamId:guid}",
            alert.EndpointPattern);
    }

    [Fact]
    public void AggregationState_IsHardBoundedAndUsesAnOverflowAlert()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecurityEventAlertSink();

        var aggregator =
            new SecurityEventAlertAggregator(
                clock,
                sink);

        for (var index = 0;
             index <
                SecurityEventAlertAggregator
                    .MaximumBucketCount +
                10;
             index++)
        {
            aggregator.Observe(
                CreateObservation(
                    SecurityEventNames
                        .AuthenticationRejected)
                    with
                    {
                        EndpointPattern =
                            $"/test-route-{index}"
                    });
        }

        Assert.Equal(
            SecurityEventAlertAggregator
                .MaximumBucketCount,
            aggregator.BucketCount);

        var capacityAlert =
            Assert.Single(
                sink.Alerts);

        Assert.Equal(
            SecurityEventAlertNames
                .AggregationCapacityReached,
            capacityAlert.Name);

        Assert.Equal(
            "<aggregate-overflow>",
            capacityAlert.EndpointPattern);

        Assert.Equal(
            "OTHER",
            capacityAlert.HttpMethod);

        Assert.False(
            capacityAlert.Authenticated);
    }

    [Fact]
    public void AlertPayload_ExcludesRequestIdsAndUserControlledRequestData()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecurityEventAlertSink();

        var aggregator =
            new SecurityEventAlertAggregator(
                clock,
                sink);

        var observation =
            CreateObservation(
                SecurityEventNames.RateLimitRejected)
            with
            {
                RequestId =
                    "secret-request-id"
            };

        Observe(
            aggregator,
            observation,
            count:
                5);

        var alert =
            Assert.Single(
                sink.Alerts);

        var serialized =
            string.Join(
                "|",
                alert.Name,
                alert.SecurityEventName,
                alert.HttpMethod,
                alert.EndpointPattern,
                alert.Authenticated,
                alert.Threshold,
                alert.WindowSeconds,
                alert.SuppressedThresholds);

        Assert.DoesNotContain(
            "secret-request-id",
            serialized,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiredBuckets_ArePrunedBeforeNewKeysAreAdded()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecurityEventAlertSink();

        var aggregator =
            new SecurityEventAlertAggregator(
                clock,
                sink);

        aggregator.Observe(
            CreateObservation(
                SecurityEventNames.RateLimitRejected));

        Assert.Equal(
            2,
            aggregator.BucketCount);

        clock.Advance(
            TimeSpan.FromMinutes(
                21));

        aggregator.Observe(
            CreateObservation(
                SecurityEventNames.RateLimitRejected)
                with
                {
                    EndpointPattern =
                        "/api/new-route"
                });

        Assert.Equal(
            2,
            aggregator.BucketCount);
    }

    private static SecurityEventObservation CreateObservation(
        string name)
    {
        return new SecurityEventObservation(
            name,
            "POST",
            "/api/bill-streams/{billStreamId:guid}",
            name switch
            {
                SecurityEventNames.AuthenticationRejected =>
                    401,

                SecurityEventNames.AuthorizationDenied =>
                    403,

                SecurityEventNames.RateLimitRejected =>
                    429,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(name))
            },
            true,
            "0123456789abcdef0123456789abcdef");
    }

    private static void Observe(
        SecurityEventAlertAggregator aggregator,
        SecurityEventObservation observation,
        int count)
    {
        for (var index = 0;
             index < count;
             index++)
        {
            aggregator.Observe(
                observation);
        }
    }

    private sealed class RecordingSecurityEventAlertSink
        : ISecurityEventAlertSink
    {
        internal List<SecurityEventAlert> Alerts { get; } =
            [];

        public void Write(
            SecurityEventAlert securityAlert)
        {
            Alerts.Add(
                securityAlert);
        }
    }

    private sealed class TestTimeProvider
        : TimeProvider
    {
        private DateTimeOffset _now =
            new(
                2026,
                10,
                2,
                0,
                0,
                0,
                TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        internal void Advance(
            TimeSpan elapsed)
        {
            _now +=
                elapsed;
        }
    }
}

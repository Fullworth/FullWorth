using FullWorth.API.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class SecuritySensitiveActionAlertAggregatorTests
{
    [Theory]
    [InlineData(
        SecurityEventNames.AdminMutationCompleted,
        "action",
        "StaffRoleAssigned",
        5,
        SecuritySensitiveActionAlertNames.RepeatedAdminMutations)]
    [InlineData(
        SecurityEventNames.AccountExportCompleted,
        "scope",
        "application",
        10,
        SecuritySensitiveActionAlertNames.RepeatedAccountExports)]
    [InlineData(
        SecurityEventNames.AccountDeletionCompleted,
        "scope",
        "application",
        3,
        SecuritySensitiveActionAlertNames.RepeatedAccountDeletions)]
    [InlineData(
        SecurityEventNames.FinancialProviderAttentionRequired,
        "operation",
        "accounts_sync",
        5,
        SecuritySensitiveActionAlertNames
            .RepeatedFinancialProviderAttention)]
    public void Thresholds_EmitOneSafeBoundedAlert(
        string securityEventName,
        string dimensionName,
        string dimensionValue,
        int threshold,
        string expectedAlertName)
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecuritySensitiveActionAlertSink();

        var aggregator =
            new SecuritySensitiveActionAlertAggregator(
                clock,
                sink);

        var observation =
            new SecuritySensitiveActionObservation(
                securityEventName,
                dimensionName,
                dimensionValue);

        Observe(
            aggregator,
            observation,
            threshold -
                1);

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
            dimensionName,
            alert.DimensionName);

        Assert.Equal(
            dimensionValue,
            alert.DimensionValue);

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
            new RecordingSecuritySensitiveActionAlertSink();

        var aggregator =
            new SecuritySensitiveActionAlertAggregator(
                clock,
                sink);

        var observation =
            new SecuritySensitiveActionObservation(
                SecurityEventNames.AccountDeletionCompleted,
                "scope",
                "application");

        Observe(
            aggregator,
            observation,
            3);

        Observe(
            aggregator,
            observation,
            3);

        Assert.Single(
            sink.Alerts);

        clock.Advance(
            SecuritySensitiveActionAlertAggregator
                .AlertCooldown);

        Observe(
            aggregator,
            observation,
            3);

        Assert.Equal(
            2,
            sink.Alerts.Count);

        Assert.Equal(
            1,
            sink.Alerts[1]
                .SuppressedThresholds);
    }

    [Fact]
    public void WindowExpiry_ResetsPartialCounts()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecuritySensitiveActionAlertSink();

        var aggregator =
            new SecuritySensitiveActionAlertAggregator(
                clock,
                sink);

        var observation =
            new SecuritySensitiveActionObservation(
                SecurityEventNames.AdminMutationCompleted,
                "action",
                "StaffRoleRemoved");

        Observe(
            aggregator,
            observation,
            4);

        clock.Advance(
            SecuritySensitiveActionAlertAggregator
                .Window);

        aggregator.Observe(
            observation);

        Assert.Empty(
            sink.Alerts);
    }

    [Fact]
    public void UnknownOrUserControlledDimensions_AreRejectedBeforeStateIsAllocated()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecuritySensitiveActionAlertSink();

        var aggregator =
            new SecuritySensitiveActionAlertAggregator(
                clock,
                sink);

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                aggregator.Observe(
                    new SecuritySensitiveActionObservation(
                        SecurityEventNames.AdminMutationCompleted,
                        "actor",
                        "attacker@example.com secret-token")));

        Assert.Equal(
            0,
            aggregator.BucketCount);

        Assert.Empty(
            sink.Alerts);
    }

    [Fact]
    public void AggregationState_IsFixedToTheAllowlistedEventDimensions()
    {
        var clock =
            new TestTimeProvider();

        var sink =
            new RecordingSecuritySensitiveActionAlertSink();

        var aggregator =
            new SecuritySensitiveActionAlertAggregator(
                clock,
                sink);

        var observations =
            new[]
            {
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "StaffRoleAssigned"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "StaffRoleRemoved"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "SubscriptionEntitlementGranted"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "SubscriptionEntitlementRevoked"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "UserProgramMembershipEnabled"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "UserProgramMembershipDisabled"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "SubscriptionAccessKeyCreated"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AdminMutationCompleted,
                    "action",
                    "SubscriptionAccessKeyRevoked"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AccountExportCompleted,
                    "scope",
                    "application"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.AccountDeletionCompleted,
                    "scope",
                    "application"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.FinancialProviderAttentionRequired,
                    "operation",
                    "accounts_sync"),
                new SecuritySensitiveActionObservation(
                    SecurityEventNames.FinancialProviderAttentionRequired,
                    "operation",
                    "transactions_sync")
            };

        foreach (var observation in
                 observations)
        {
            aggregator.Observe(
                observation);
        }

        Assert.Equal(
            observations.Length,
            aggregator.BucketCount);

        Assert.Empty(
            sink.Alerts);
    }

    private static void Observe(
        SecuritySensitiveActionAlertAggregator aggregator,
        SecuritySensitiveActionObservation observation,
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

    private sealed class
        RecordingSecuritySensitiveActionAlertSink
        : ISecuritySensitiveActionAlertSink
    {
        internal List<SecuritySensitiveActionAlert> Alerts { get; } =
            [];

        public void Write(
            SecuritySensitiveActionAlert securityAlert)
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

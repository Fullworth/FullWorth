using FullWorth.API.Infrastructure;

namespace FullWorth.Tests.Infrastructure;

internal static class TestSecurityAlertAggregator
{
    internal static SecuritySensitiveActionAlertAggregator Create()
    {
        return new SecuritySensitiveActionAlertAggregator(
            TimeProvider.System,
            new NullSecuritySensitiveActionAlertSink());
    }

    private sealed class NullSecuritySensitiveActionAlertSink
        : ISecuritySensitiveActionAlertSink
    {
        public void Write(
            SecuritySensitiveActionAlert securityAlert)
        {
        }
    }
}

using System.Diagnostics.Metrics;

namespace FullWorth.API.Authorization;

public sealed class SubscriptionAuthorizationTelemetry
{
    private readonly Counter<long> _denials =
        new Meter("FullWorth.Authorization")
            .CreateCounter<long>("billwatch.subscription.denials");

    public void RecordDenial(string reason)
    {
        _denials.Add(1, new KeyValuePair<string, object?>("reason", reason));
    }
}

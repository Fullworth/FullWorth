namespace FullWorth.Web.Services;

/// <summary>Only observed active bank syncs count; disconnected or attention states do not.</summary>
public readonly record struct OverviewConnectionFact(
    int Status, DateTimeOffset? LastSuccessfulSyncAtUtc);

public sealed record OverviewConnectionCoverage(
    int ActiveCount, int SyncedCount, DateTimeOffset? MostRecentSuccessfulSync)
{
    public int NeverSyncedCount => ActiveCount - SyncedCount;

    // A second newly added bank must not put an already-synced household
    // back into an inaccurate "preparing your first scan" state.
    public bool WaitingForInitialSync => ActiveCount > 0 && SyncedCount == 0;

    public bool MixedSyncCoverage => SyncedCount > 0 && NeverSyncedCount > 0;

    public static OverviewConnectionCoverage Create(
        IEnumerable<OverviewConnectionFact> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Status 0 is the Web/BFF Active state, matching Overview's existing
        // server-contract mapping. Attention and disconnected are excluded.
        var active = source.Where(connection => connection.Status == 0).ToArray();
        var successful = active
            .Where(connection => connection.LastSuccessfulSyncAtUtc.HasValue)
            .Select(connection => connection.LastSuccessfulSyncAtUtc!.Value)
            .ToArray();
        return new OverviewConnectionCoverage(
            active.Length,
            successful.Length,
            successful.Length > 0 ? successful.Max() : null);
    }
}

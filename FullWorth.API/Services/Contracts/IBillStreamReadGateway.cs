using FullWorth.Core.Models;

namespace FullWorth.API.Services.Contracts;

public sealed record BillStreamReadRecord(
    Guid BillStreamId,
    string ProviderName,
    BillCategory Category);

public interface IBillStreamReadGateway
{
    Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    // Historic plan rows must retain readable names even if a user stops
    // monitoring a bill. The production adapter performs a single owner-scoped query.
    // Default implementation preserves compatibility with test adapters.
    async Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedByIdsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> billStreamIds,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required.", nameof(userId));
        ArgumentNullException.ThrowIfNull(billStreamIds);
        var results = new List<BillStreamReadRecord>();
        foreach (var id in billStreamIds.Distinct())
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Bill stream IDs must not be empty.", nameof(billStreamIds));
            var bill = await GetOwnedAsync(userId, id, cancellationToken);
            if (bill is not null)
                results.Add(bill);
        }
        return results;
    }

    Task<BillStreamReadRecord?> GetOwnedAsync(
        Guid userId,
        Guid billStreamId,
        CancellationToken cancellationToken = default);
}

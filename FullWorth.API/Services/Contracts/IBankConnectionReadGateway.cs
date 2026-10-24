namespace FullWorth.API.Services.Contracts;

public interface IBankConnectionReadGateway
{
    // Continue after a stable user identity, not a position in the changing due set.
    Task<IReadOnlyList<Guid>> GetDueUserIdsAfterAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        Guid? afterUserId,
        CancellationToken cancellationToken = default);

    // Providers should push paging into their data store. The default keeps
    // existing providers compatible with the background sweep contract.
    async Task<IReadOnlyList<Guid>> GetDueUserIdsPageAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (maxUsers <= 0) throw new ArgumentOutOfRangeException(nameof(maxUsers));
        var users = await GetDueUserIdsAsync(
            now, refreshCadence, checked(offset + maxUsers), cancellationToken);
        return users.Skip(offset).Take(maxUsers).ToArray();
    }

    Task<IReadOnlyList<string>> GetAttentionInstitutionNamesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetDueUserIdsAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        CancellationToken cancellationToken = default);
}

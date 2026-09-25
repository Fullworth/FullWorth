namespace FullWorth.API.Services.Contracts;

public interface IBankConnectionReadGateway
{
    Task<IReadOnlyList<string>> GetAttentionInstitutionNamesAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetDueUserIdsAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        CancellationToken cancellationToken = default);
}

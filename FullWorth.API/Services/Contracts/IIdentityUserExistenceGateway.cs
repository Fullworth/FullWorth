namespace FullWorth.API.Services.Contracts;

public interface IIdentityUserExistenceGateway
{
    Task<bool> ExistsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlySet<Guid>> GetExistingUserIdsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);
}

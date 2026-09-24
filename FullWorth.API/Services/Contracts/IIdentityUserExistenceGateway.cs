namespace FullWorth.API.Services.Contracts;

public interface IIdentityUserExistenceGateway
{
    Task<bool> ExistsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

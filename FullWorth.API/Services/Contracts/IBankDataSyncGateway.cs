namespace FullWorth.API.Services.Contracts;

public interface IBankDataSyncGateway
{
    Task SyncAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task SyncTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

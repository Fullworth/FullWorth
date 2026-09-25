using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankDataSyncGateway(
    PlaidConnectionSyncCoordinator coordinator)
    : IBankDataSyncGateway
{
    public async Task SyncAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await coordinator.SyncAllAccountsAsync(
            userId,
            cancellationToken);
    }

    public async Task SyncTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        await coordinator.SyncAllTransactionsAsync(
            userId,
            cancellationToken);
    }
}

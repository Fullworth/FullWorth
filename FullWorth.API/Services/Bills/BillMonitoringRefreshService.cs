using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Bills;

public sealed class BillMonitoringRefreshService
{
    private readonly IBankDataSyncGateway _bankDataSyncGateway;
    private readonly RecurringBillDiscoveryPersistenceService _billDiscoveryService;
    private readonly BankConnectionHealthAlertService _connectionHealthAlertService;
    private readonly ILogger<BillMonitoringRefreshService> _logger;

    public BillMonitoringRefreshService(
        IBankDataSyncGateway bankDataSyncGateway,
        RecurringBillDiscoveryPersistenceService billDiscoveryService,
        BankConnectionHealthAlertService connectionHealthAlertService,
        ILogger<BillMonitoringRefreshService> logger)
    {
        ArgumentNullException.ThrowIfNull(
            bankDataSyncGateway);

        ArgumentNullException.ThrowIfNull(
            billDiscoveryService);

        ArgumentNullException.ThrowIfNull(
            connectionHealthAlertService);

        ArgumentNullException.ThrowIfNull(
            logger);

        _bankDataSyncGateway =
            bankDataSyncGateway;

        _billDiscoveryService =
            billDiscoveryService;

        _connectionHealthAlertService =
            connectionHealthAlertService;

        _logger =
            logger;
    }

    public async Task<RecurringBillDiscoveryPersistenceResult> RefreshAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        try
        {
            /*
             * Always synchronize accounts first.
             *
             * A brand-new provider connection may exist before FullWorth has
             * persisted its checking/credit/etc. accounts. Transaction sync
             * depends on those local BankAccount rows.
             *
             * Bills intentionally depends on the provider-neutral contract,
             * not on Plaid implementation types.
             */
            await _bankDataSyncGateway.SyncAccountsAsync(
                userId,
                cancellationToken);

            /*
             * Pull new/modified/removed transactions through the same
             * provider-neutral bank synchronization boundary.
             */
            await _bankDataSyncGateway.SyncTransactionsAsync(
                userId,
                cancellationToken);

            /*
             * Re-run deterministic recurring-bill discovery against the
             * newly synchronized transaction history.
             */
            var result =
                await _billDiscoveryService.DiscoverAndSaveAsync(
                    userId,
                    cancellationToken);

            /*
             * Connection alerts are secondary to the core refresh.
             * Failure to create an Activity alert must never cause a
             * successful financial-data refresh to be reported as failed.
             */
            await TryReconcileConnectionHealthAsync(
                userId,
                cancellationToken);

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            /*
             * A provider implementation may persist RequiresAttention before
             * propagating a provider error. Reconcile that state into the
             * Activity feed, but preserve the original refresh exception.
             */
            await TryReconcileConnectionHealthAsync(
                userId,
                CancellationToken.None);

            throw;
        }
    }

    private async Task TryReconcileConnectionHealthAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _connectionHealthAlertService.ReconcileAsync(
                userId,
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            /*
             * Never log account data, tokens, connection IDs, institution
             * lists, or financial information here.
             */
            _logger.LogWarning(
                "Bank connection health alert reconciliation failed with {ExceptionType}.",
                ex.GetType().Name);
        }
    }
}

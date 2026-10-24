using System.Runtime.ExceptionServices;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankDataSyncGateway(
    FullWorthDbContext dbContext,
    IServiceScopeFactory scopeFactory)
    : IBankDataSyncGateway
{
    public async Task SyncAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await SyncConnectionsAsync(userId, syncAccounts: true, syncTransactions: true, cancellationToken);
    }

    public async Task SyncAccountsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await SyncAllAccountsAsync(userId, cancellationToken);
    }

    public async Task SyncTransactionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await SyncAllTransactionsAsync(userId, cancellationToken);
    }

    public async Task<PlaidAccountSyncSummary> SyncAllAccountsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var totals = await SyncConnectionsAsync(
            userId, syncAccounts: true, syncTransactions: false, cancellationToken);
        return new PlaidAccountSyncSummary(totals.Connections, totals.Accounts);
    }

    public async Task<PlaidTransactionSyncSummary> SyncAllTransactionsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var totals = await SyncConnectionsAsync(
            userId, syncAccounts: false, syncTransactions: true, cancellationToken);
        return new PlaidTransactionSyncSummary(
            totals.Connections, totals.Added, totals.Modified, totals.Removed);
    }

    private async Task<SyncTotals> SyncConnectionsAsync(
        Guid userId, bool syncAccounts, bool syncTransactions, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A valid user ID is required.", nameof(userId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Read IDs only in the caller's context. Every bank gets a fresh
        // context, so one failed save cannot be committed by a later bank.
        var connectionIds = await dbContext.BankConnections
            .AsNoTracking()
            .Where(connection =>
                connection.UserId == userId &&
                connection.Status == BankConnectionStatus.Active &&
                connection.ProtectedPlaidAccessToken != null &&
                connection.ProtectedPlaidAccessToken != string.Empty)
            .OrderBy(connection => connection.Id)
            .Select(connection => connection.Id)
            .ToListAsync(cancellationToken);

        ExceptionDispatchInfo? firstFailure = null;
        var accounts = 0;
        var added = 0;
        var modified = 0;
        var removed = 0;

        foreach (var connectionId in connectionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var coordinator = scope.ServiceProvider
                    .GetRequiredService<PlaidConnectionSyncCoordinator>();

                if (syncAccounts)
                {
                    accounts += await coordinator.SyncAccountsAsync(
                        userId, connectionId, cancellationToken);
                }

                if (syncTransactions)
                {
                    var result = await coordinator.SyncTransactionsAsync(
                        userId, connectionId, cancellationToken);
                    added += result.Added;
                    modified += result.Modified;
                    removed += result.Removed;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Attempt other banks once, then preserve the original error.
                // Neither HTTP callers nor discovery may treat partial work
                // as a successfully completed refresh.
                firstFailure ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        firstFailure?.Throw();
        return new SyncTotals(connectionIds.Count, accounts, added, modified, removed);
    }

    private sealed record SyncTotals(
        int Connections, int Accounts, int Added, int Modified, int Removed);
}

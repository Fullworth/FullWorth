using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidConnectionSyncCoordinator(
    FullWorthDbContext dbContext,
    PlaidAccountSyncService accountSyncService,
    PlaidTransactionSyncService transactionSyncService,
    ILoggerFactory loggerFactory,
    SecuritySensitiveActionAlertAggregator
        securityAlertAggregator)
{
    private readonly ILogger _securityLogger =
        SecuritySensitiveActionLog.CreateLogger(
            loggerFactory);
    public async Task<PlaidAccountSyncSummary> SyncAllAccountsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var connections =
            await GetActiveConnectionsAsync(
                userId,
                cancellationToken);

        var totalAccountsSynced = 0;

        foreach (var connection in connections)
        {
            try
            {
                totalAccountsSynced +=
                    await accountSyncService.SyncAccountsAsync(
                        userId,
                        connection,
                        cancellationToken);
            }
            catch (PlaidApiException exception)
                when (PlaidConnectionAttentionClassifier
                    .RequiresUserAttention(
                        exception))
            {
                if (await PersistRequiresAttentionAsync(
                        userId,
                        connection,
                        cancellationToken))
                {
                    SecuritySensitiveActionLog
                        .FinancialProviderAttentionRequired(
                            _securityLogger,
                            securityAlertAggregator,
                            "accounts_sync");
                }

                throw;
            }
        }

        return new PlaidAccountSyncSummary(
            connections.Count,
            totalAccountsSynced);
    }

    public async Task<PlaidTransactionSyncSummary> SyncAllTransactionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var connections =
            await GetActiveConnectionsAsync(
                userId,
                cancellationToken);

        var totalAdded = 0;
        var totalModified = 0;
        var totalRemoved = 0;

        foreach (var connection in
                 connections)
        {
            try
            {
                var result =
                    await transactionSyncService
                        .SyncConnectionAsync(
                            userId,
                            connection,
                            cancellationToken);

                totalAdded += result.Added;
                totalModified += result.Modified;
                totalRemoved += result.Removed;
            }
            catch (PlaidApiException exception)
                when (PlaidConnectionAttentionClassifier
                    .RequiresUserAttention(
                        exception))
            {
                if (await PersistRequiresAttentionAsync(
                        userId,
                        connection,
                        cancellationToken))
                {
                    SecuritySensitiveActionLog
                        .FinancialProviderAttentionRequired(
                            _securityLogger,
                            securityAlertAggregator,
                            "transactions_sync");
                }

                throw;
            }
        }

        return new PlaidTransactionSyncSummary(
            connections.Count,
            totalAdded,
            totalModified,
            totalRemoved);
    }

    public async Task<int> SyncAccountsAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(userId, connectionId);

        try
        {
            return await accountSyncService.SyncAccountsAsync(
                userId,
                connectionId,
                cancellationToken);
        }
        catch (PlaidApiException exception)
            when (PlaidConnectionAttentionClassifier.RequiresUserAttention(exception))
        {
            if (await PersistRequiresAttentionAsync(
                    userId,
                    connectionId,
                    cancellationToken))
            {
                SecuritySensitiveActionLog
                    .FinancialProviderAttentionRequired(
                        _securityLogger,
                        securityAlertAggregator,
                        "accounts_sync");
            }

            throw;
        }
    }

    public async Task<PlaidTransactionConnectionSyncResult> SyncTransactionsAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifiers(userId, connectionId);

        try
        {
            return await transactionSyncService.SyncConnectionAsync(
                userId,
                connectionId,
                cancellationToken);
        }
        catch (PlaidApiException exception)
            when (PlaidConnectionAttentionClassifier.RequiresUserAttention(exception))
        {
            if (await PersistRequiresAttentionAsync(
                    userId,
                    connectionId,
                    cancellationToken))
            {
                SecuritySensitiveActionLog
                    .FinancialProviderAttentionRequired(
                        _securityLogger,
                        securityAlertAggregator,
                        "transactions_sync");
            }

            throw;
        }
    }

    private async Task<List<BankConnectionEntity>>
        GetActiveConnectionsAsync(
            Guid userId,
            CancellationToken cancellationToken)
    {
        return await dbContext.BankConnections
            .Where(connection =>
                connection.UserId == userId &&
                connection.Status == BankConnectionStatus.Active &&
                connection.ProtectedPlaidAccessToken != null &&
                connection.ProtectedPlaidAccessToken != string.Empty)
            .OrderBy(connection => connection.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> PersistRequiresAttentionAsync(
        Guid userId,
        BankConnectionEntity connection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (connection.UserId !=
            userId)
        {
            throw new InvalidOperationException(
                "The bank connection does not belong to the requested user.");
        }

        if (connection.Status !=
            BankConnectionStatus.Active)
        {
            return false;
        }

        connection.Status =
            BankConnectionStatus.RequiresAttention;

        connection.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private async Task<bool> PersistRequiresAttentionAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connection =
            await dbContext.BankConnections.SingleOrDefaultAsync(
                candidate =>
                    candidate.Id == connectionId &&
                    candidate.UserId == userId,
                cancellationToken);

        if (connection is null ||
            connection.Status != BankConnectionStatus.Active)
        {
            return false;
        }

        connection.Status =
            BankConnectionStatus.RequiresAttention;

        connection.UpdatedAtUtc =
            DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static void ValidateIdentifiers(
        Guid userId,
        Guid connectionId)
    {
        ValidateUserId(userId);

        if (connectionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid bank connection ID is required.",
                nameof(connectionId));
        }
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }
    }
}

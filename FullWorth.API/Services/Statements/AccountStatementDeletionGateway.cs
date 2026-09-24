using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class AccountStatementDeletionGateway(
    FullWorthDbContext dbContext,
    SecureBillStatementStorageService statementStorage,
    ILogger<AccountStatementDeletionGateway> logger)
    : IAccountStatementDeletionGateway
{
    public async Task<IReadOnlyList<AccountStatementQuarantineEntry>>
        QuarantineOwnedFilesAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        var storageKeys =
            await dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(
                    upload =>
                        upload.UserId ==
                            userId)
                .Select(
                    upload =>
                        upload.StorageKey)
                .Distinct()
                .ToListAsync(
                    cancellationToken);

        var quarantined =
            new List<
                AccountStatementQuarantineEntry>(
                    storageKeys.Count);

        try
        {
            foreach (var storageKey in
                     storageKeys)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entry =
                    statementStorage
                        .QuarantineForAccountDeletion(
                            userId,
                            storageKey);

                quarantined.Add(
                    ToContractEntry(
                        entry));
            }

            return quarantined.AsReadOnly();
        }
        catch (Exception exception)
            when (exception is
                IOException or
                UnauthorizedAccessException or
                InvalidOperationException or
                ArgumentException)
        {
            RestoreBestEffort(
                quarantined);

            throw;
        }
    }

    public void RestoreQuarantine(
        AccountStatementQuarantineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(
            entry);

        statementStorage
            .RestoreAccountDeletionQuarantine(
                ToStorageEntry(
                    entry));
    }

    public void CommitQuarantine(
        AccountStatementQuarantineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(
            entry);

        statementStorage
            .CommitAccountDeletionQuarantine(
                ToStorageEntry(
                    entry));
    }

    public async Task ApplyOwnedDataDeletionAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        var lineItems =
            await dbContext.BillLineItems
                .Where(
                    item =>
                        item.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var uploads =
            await dbContext.BillStatementUploads
                .Where(
                    upload =>
                        upload.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var aiEvaluations =
            await dbContext.BillStatementAiEvaluations
                .Where(
                    evaluation =>
                        evaluation.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        var changes =
            await dbContext.BillChanges
                .Where(
                    change =>
                        change.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BillLineItems.RemoveRange(
            lineItems);

        dbContext.BillStatementAiEvaluations
            .RemoveRange(
                aiEvaluations);

        dbContext.BillStatementUploads.RemoveRange(
            uploads);

        dbContext.BillChanges.RemoveRange(
            changes);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        var statements =
            await dbContext.BillStatements
                .Where(
                    statement =>
                        statement.UserId ==
                            userId)
                .ToListAsync(
                    cancellationToken);

        dbContext.BillStatements.RemoveRange(
            statements);

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private void RestoreBestEffort(
        IEnumerable<AccountStatementQuarantineEntry> entries)
    {
        foreach (var entry in
                 entries.Reverse())
        {
            try
            {
                RestoreQuarantine(
                    entry);
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                logger.LogCritical(
                    "FullWorth could not immediately restore a quarantined statement after account deletion preparation failed because of {ExceptionType}. Startup maintenance will retry recovery.",
                    exception.GetType().Name);
            }
        }
    }

    private static AccountStatementQuarantineEntry
        ToContractEntry(
            BillStatementDeletionQuarantineEntry entry)
    {
        return new AccountStatementQuarantineEntry(
            entry.UserId,
            entry.StorageKey,
            entry.WasPresent);
    }

    private static BillStatementDeletionQuarantineEntry
        ToStorageEntry(
            AccountStatementQuarantineEntry entry)
    {
        return new BillStatementDeletionQuarantineEntry(
            entry.UserId,
            entry.StorageKey,
            entry.WasPresent);
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}

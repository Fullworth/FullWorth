using FullWorth.API.Data;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class AccountDeletionStatementQuarantineRecovery(
    FullWorthDbContext dbContext,
    SecureBillStatementStorageService statementStorage,
    ILogger<AccountDeletionStatementQuarantineRecovery> logger)
{
    public async Task<int> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        var entries =
            statementStorage.GetPendingAccountDeletionQuarantineEntries();

        if (entries.Count == 0)
        {
            return 0;
        }

        var quarantinedUserIds =
            entries
                .Select(
                    entry =>
                        entry.UserId)
                .Distinct()
                .ToArray();

        var existingUserIds =
            await dbContext.Users
                .AsNoTracking()
                .Where(
                    user =>
                        quarantinedUserIds.Contains(
                            user.Id))
                .Select(
                    user =>
                        user.Id)
                .ToHashSetAsync(
                    cancellationToken);

        var reconciled = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (existingUserIds.Contains(
                        entry.UserId))
                {
                    statementStorage.RestoreAccountDeletionQuarantine(
                        entry);

                    logger.LogWarning(
                        "Restored a quarantined statement after an interrupted account deletion for user {UserId}.",
                        entry.UserId);
                }
                else
                {
                    statementStorage.CommitAccountDeletionQuarantine(
                        entry);

                    logger.LogInformation(
                        "Purged a quarantined statement after committed account deletion for user {UserId}.",
                        entry.UserId);
                }

                reconciled++;
            }
            catch (Exception exception)
                when (exception is
                    IOException or
                    UnauthorizedAccessException or
                    InvalidOperationException or
                    ArgumentException)
            {
                logger.LogError(
                    "Account-deletion statement quarantine reconciliation failed for user {UserId} with {ExceptionType}.",
                    entry.UserId,
                    exception.GetType().Name);
            }
        }

        return reconciled;
    }
}

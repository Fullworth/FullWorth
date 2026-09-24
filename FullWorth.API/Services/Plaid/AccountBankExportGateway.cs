using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class AccountBankExportGateway(
    FullWorthDbContext dbContext)
    : IAccountBankExportGateway
{
    public async Task<AccountBankExportSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var connections =
            await dbContext.BankConnections
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var accounts =
            await dbContext.BankAccounts
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var transactions =
            await dbContext.BankTransactions
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.PostedDate)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var linkSessions =
            await dbContext.PlaidLinkSessions
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        return new AccountBankExportSnapshot(
            connections
                .Select(item => new AccountBankConnectionExportRecord(
                    item.Id,
                    item.InstitutionName,
                    item.Status.ToString(),
                    item.LastSuccessfulSyncAtUtc,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            accounts
                .Select(item => new AccountBankAccountExportRecord(
                    item.Id,
                    item.BankConnectionId,
                    item.Name,
                    item.OfficialName,
                    item.Mask,
                    item.AccountType.ToString(),
                    item.AccountSubtype,
                    item.IsActive,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            transactions
                .Select(item => new AccountBankTransactionExportRecord(
                    item.Id,
                    item.BankAccountId,
                    item.BillStreamId,
                    item.Name,
                    item.MerchantName,
                    item.Amount,
                    item.IsoCurrencyCode,
                    item.PostedDate,
                    item.AuthorizedDate,
                    item.IsPending,
                    item.IsRemoved,
                    item.CategoryPrimary,
                    item.CategoryDetailed,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            linkSessions
                .Select(item => new AccountPlaidLinkSessionExportRecord(
                    item.Id,
                    item.Status.ToString(),
                    item.ExpiresAtUtc,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc,
                    item.CompletedAtUtc))
                .ToArray());
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}

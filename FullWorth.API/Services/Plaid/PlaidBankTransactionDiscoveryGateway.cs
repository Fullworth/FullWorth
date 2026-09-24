using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankTransactionDiscoveryGateway(
    FullWorthDbContext dbContext)
    : IBankTransactionDiscoveryGateway
{
    public async Task<IReadOnlyList<BankTransactionDiscoveryRecord>>
        GetDiscoveryTransactionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        return await dbContext.BankTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.UserId ==
                        userId &&
                    !transaction.IsRemoved)
            .OrderBy(
                transaction =>
                    transaction.PostedDate)
            .ThenBy(
                transaction =>
                    transaction.Id)
            .Select(
                transaction =>
                    new BankTransactionDiscoveryRecord(
                        transaction.Id,
                        transaction.Name,
                        transaction.MerchantName,
                        transaction.Amount,
                        transaction.PostedDate,
                        transaction.IsPending,
                        transaction.CategoryPrimary,
                        transaction.CategoryDetailed))
            .ToListAsync(
                cancellationToken);
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

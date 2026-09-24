using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankTransactionMetricReadGateway(
    FullWorthDbContext dbContext)
    : IBankTransactionMetricReadGateway
{
    public async Task<IReadOnlyList<BankTransactionMetricRecord>>
        GetTransactionsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> transactionIds,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            transactionIds);

        if (transactionIds.Count ==
            0)
        {
            return [];
        }

        var ids =
            new HashSet<Guid>();

        foreach (var transactionId in
                 transactionIds)
        {
            if (transactionId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction IDs must not be empty.",
                    nameof(transactionIds));
            }

            ids.Add(
                transactionId);
        }

        return await dbContext.BankTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.UserId ==
                        userId &&
                    ids.Contains(
                        transaction.Id))
            .Select(
                transaction =>
                    new BankTransactionMetricRecord(
                        transaction.Id,
                        transaction.Amount,
                        transaction.PostedDate,
                        transaction.CreatedAtUtc,
                        transaction.IsPending,
                        transaction.IsRemoved))
            .ToListAsync(
                cancellationToken);
    }
}

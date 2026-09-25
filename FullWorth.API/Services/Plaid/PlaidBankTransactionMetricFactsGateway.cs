using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankTransactionMetricFactsGateway(
    FullWorthDbContext dbContext)
    : IBankTransactionMetricFactsGateway
{
    public async Task<IReadOnlyList<BankTransactionMetricFact>> GetAsync(
        Guid userId,
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(transactionIds);

        if (transactionIds.Count == 0)
        {
            return Array.Empty<BankTransactionMetricFact>();
        }

        var ids = transactionIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length != transactionIds.Distinct().Count())
        {
            throw new ArgumentException(
                "Transaction IDs must not be empty.",
                nameof(transactionIds));
        }

        return await dbContext.BankTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.UserId == userId &&
                    ids.Contains(transaction.Id) &&
                    !transaction.IsRemoved &&
                    !transaction.IsPending)
            .Select(
                transaction =>
                    new BankTransactionMetricFact(
                        transaction.Id,
                        transaction.Amount,
                        transaction.PostedDate,
                        transaction.CreatedAtUtc))
            .ToListAsync(cancellationToken);
    }
}

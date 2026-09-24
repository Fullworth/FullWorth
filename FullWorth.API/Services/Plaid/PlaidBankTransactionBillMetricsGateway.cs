using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankTransactionBillMetricsGateway(
    FullWorthDbContext dbContext)
    : IBankTransactionBillMetricsGateway
{
    public async Task<IReadOnlyDictionary<Guid, BankTransactionBillStreamMetrics>>
        GetMetricsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            billStreamIds);

        if (billStreamIds.Count == 0)
        {
            return new Dictionary<Guid, BankTransactionBillStreamMetrics>();
        }

        var uniqueIds =
            new HashSet<Guid>();

        foreach (var billStreamId in billStreamIds)
        {
            if (billStreamId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Bill Stream IDs must not be empty.",
                    nameof(billStreamIds));
            }

            uniqueIds.Add(
                billStreamId);
        }

        var ids =
            uniqueIds.ToArray();

        var metrics =
            await dbContext.BankTransactions
                .AsNoTracking()
                .Where(
                    transaction =>
                        transaction.UserId ==
                            userId &&
                        transaction.BillStreamId !=
                            null &&
                        ids.Contains(
                            transaction.BillStreamId.Value) &&
                        !transaction.IsRemoved &&
                        !transaction.IsPending)
                .GroupBy(
                    transaction =>
                        transaction.BillStreamId!.Value)
                .Select(
                    group =>
                        new BankTransactionBillStreamMetrics(
                            group.Key,
                            group
                                .OrderByDescending(
                                    transaction =>
                                        transaction.PostedDate)
                                .ThenByDescending(
                                    transaction =>
                                        transaction.CreatedAtUtc)
                                .Select(
                                    transaction =>
                                        transaction.Amount)
                                .First(),
                            group.Count() <= 1
                                ? 0m
                                : group
                                    .OrderByDescending(
                                        transaction =>
                                            transaction.PostedDate)
                                    .ThenByDescending(
                                        transaction =>
                                            transaction.CreatedAtUtc)
                                    .Skip(1)
                                    .Average(
                                        transaction =>
                                            transaction.Amount)))
                .ToListAsync(
                    cancellationToken);

        return metrics.ToDictionary(
            metric =>
                metric.BillStreamId);
    }
}

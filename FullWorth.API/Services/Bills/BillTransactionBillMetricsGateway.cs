using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class BillTransactionBillMetricsGateway(
    FullWorthDbContext dbContext,
    IBankTransactionMetricReadGateway transactionMetricGateway)
    : IBankTransactionBillMetricsGateway
{
    public async Task<IReadOnlyDictionary<Guid, BankTransactionBillStreamMetrics>>
        GetMetricsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
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
            billStreamIds);

        if (billStreamIds.Count ==
            0)
        {
            return new Dictionary<Guid, BankTransactionBillStreamMetrics>();
        }

        var uniqueStreamIds =
            new HashSet<Guid>();

        foreach (var billStreamId in
                 billStreamIds)
        {
            if (billStreamId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "Bill Stream IDs must not be empty.",
                    nameof(billStreamIds));
            }

            uniqueStreamIds.Add(
                billStreamId);
        }

        var streamIds =
            uniqueStreamIds.ToArray();

        var links =
            await dbContext.BillTransactionLinks
                .AsNoTracking()
                .Where(
                    link =>
                        link.UserId ==
                            userId &&
                        streamIds.Contains(
                            link.BillStreamId))
                .ToListAsync(
                    cancellationToken);

        if (links.Count ==
            0)
        {
            return new Dictionary<Guid, BankTransactionBillStreamMetrics>();
        }

        var transactionIds =
            links
                .Select(
                    link =>
                        link.BankTransactionId)
                .ToArray();

        var transactions =
            await transactionMetricGateway
                .GetTransactionsAsync(
                    userId,
                    transactionIds,
                    cancellationToken);

        var transactionsById =
            transactions
                .Where(
                    transaction =>
                        !transaction.IsRemoved &&
                        !transaction.IsPending)
                .ToDictionary(
                    transaction =>
                        transaction.TransactionId);

        var metrics =
            new Dictionary<Guid, BankTransactionBillStreamMetrics>();

        foreach (var group in
                 links.GroupBy(
                     link =>
                         link.BillStreamId))
        {
            var orderedTransactions =
                group
                    .Select(
                        link =>
                            transactionsById.TryGetValue(
                                link.BankTransactionId,
                                out var transaction)
                                ? transaction
                                : null)
                    .OfType<BankTransactionMetricRecord>()
                    .OrderByDescending(
                        transaction =>
                            transaction.PostedDate)
                    .ThenByDescending(
                        transaction =>
                            transaction.CreatedAtUtc)
                    .ToList();

            if (orderedTransactions.Count ==
                0)
            {
                continue;
            }

            var currentAmount =
                orderedTransactions[0].Amount;

            var previousAverage =
                orderedTransactions.Count <=
                    1
                    ? 0m
                    : orderedTransactions
                        .Skip(
                            1)
                        .Average(
                            transaction =>
                                transaction.Amount);

            metrics.Add(
                group.Key,
                new BankTransactionBillStreamMetrics(
                    group.Key,
                    currentAmount,
                    previousAverage));
        }

        return metrics;
    }
}

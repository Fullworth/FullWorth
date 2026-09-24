using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class BillTransactionMetricsGateway(
    FullWorthDbContext dbContext,
    IBankTransactionMetricFactsGateway transactionFactsGateway)
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

        ArgumentNullException.ThrowIfNull(billStreamIds);

        if (billStreamIds.Count == 0)
        {
            return new Dictionary<Guid, BankTransactionBillStreamMetrics>();
        }

        var ids = billStreamIds
            .Distinct()
            .ToArray();

        if (ids.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "Bill Stream IDs must not be empty.",
                nameof(billStreamIds));
        }

        var associations =
            await dbContext.BillTransactionAssociations
                .AsNoTracking()
                .Where(
                    association =>
                        association.UserId == userId &&
                        ids.Contains(association.BillStreamId))
                .Select(
                    association =>
                        new
                        {
                            association.BankTransactionId,
                            association.BillStreamId
                        })
                .ToListAsync(cancellationToken);

        if (associations.Count == 0)
        {
            return new Dictionary<Guid, BankTransactionBillStreamMetrics>();
        }

        var facts =
            await transactionFactsGateway.GetAsync(
                userId,
                associations
                    .Select(association => association.BankTransactionId)
                    .ToArray(),
                cancellationToken);

        var billStreamIdByTransactionId =
            associations.ToDictionary(
                association => association.BankTransactionId,
                association => association.BillStreamId);

        return facts
            .Where(
                fact =>
                    billStreamIdByTransactionId.ContainsKey(
                        fact.TransactionId))
            .GroupBy(
                fact =>
                    billStreamIdByTransactionId[fact.TransactionId])
            .Select(
                group =>
                {
                    var ordered = group
                        .OrderByDescending(fact => fact.PostedDate)
                        .ThenByDescending(fact => fact.CreatedAtUtc)
                        .ToArray();

                    return new BankTransactionBillStreamMetrics(
                        group.Key,
                        ordered[0].Amount,
                        ordered.Length <= 1
                            ? 0m
                            : ordered
                                .Skip(1)
                                .Average(fact => fact.Amount));
                })
            .ToDictionary(metric => metric.BillStreamId);
    }
}

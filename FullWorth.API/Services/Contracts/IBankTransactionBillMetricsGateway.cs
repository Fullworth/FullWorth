namespace FullWorth.API.Services.Contracts;

public sealed record BankTransactionMetricRecord(
    Guid TransactionId,
    decimal Amount,
    DateOnly PostedDate,
    DateTimeOffset CreatedAtUtc,
    bool IsPending,
    bool IsRemoved);

public interface IBankTransactionMetricReadGateway
{
    Task<IReadOnlyList<BankTransactionMetricRecord>>
        GetTransactionsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> transactionIds,
            CancellationToken cancellationToken = default);
}

public sealed record BankTransactionBillStreamMetrics(
    Guid BillStreamId,
    decimal CurrentAmount,
    decimal PreviousAverage);

public interface IBankTransactionBillMetricsGateway
{
    Task<IReadOnlyDictionary<Guid, BankTransactionBillStreamMetrics>>
        GetMetricsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            CancellationToken cancellationToken = default);
}

namespace FullWorth.API.Services.Contracts;

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

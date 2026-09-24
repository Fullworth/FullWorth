namespace FullWorth.API.Services.Contracts;

public sealed record BankTransactionMetricFact(
    Guid TransactionId,
    decimal Amount,
    DateOnly PostedDate,
    DateTimeOffset CreatedAtUtc);

public interface IBankTransactionMetricFactsGateway
{
    Task<IReadOnlyList<BankTransactionMetricFact>> GetAsync(
        Guid userId,
        IReadOnlyCollection<Guid> transactionIds,
        CancellationToken cancellationToken = default);
}

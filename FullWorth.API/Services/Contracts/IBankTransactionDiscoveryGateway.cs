namespace FullWorth.API.Services.Contracts;

public sealed record BankTransactionDiscoveryRecord(
    Guid TransactionId,
    string Name,
    string? MerchantName,
    decimal Amount,
    DateOnly PostedDate,
    bool IsPending,
    string? CategoryPrimary,
    string? CategoryDetailed);

public interface IBankTransactionDiscoveryGateway
{
    Task<IReadOnlyList<BankTransactionDiscoveryRecord>>
        GetDiscoveryTransactionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
}

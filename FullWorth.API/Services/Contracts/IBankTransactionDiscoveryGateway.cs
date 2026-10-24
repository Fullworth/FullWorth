namespace FullWorth.API.Services.Contracts;

public sealed record BankTransactionDiscoveryRecord(
    Guid TransactionId,
    string Name,
    string? MerchantName,
    decimal Amount,
    DateOnly PostedDate,
    bool IsPending,
    string? CategoryPrimary,
    string? CategoryDetailed)
{
    public string? CurrencyCode { get; init; }

    // Opaque internal owner-scoped evidence, never part of public diagnostics.
    public Guid? BankAccountId { get; init; }
}

public interface IBankTransactionDiscoveryGateway
{
    Task<IReadOnlyList<BankTransactionDiscoveryRecord>>
        GetDiscoveryTransactionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);
}

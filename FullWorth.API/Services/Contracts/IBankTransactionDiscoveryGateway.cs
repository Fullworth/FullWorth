namespace FullWorth.API.Services.Contracts;

public sealed record BankTransactionDiscoveryRecord(
    Guid TransactionId,
    Guid? BillStreamId,
    string Name,
    string? MerchantName,
    decimal Amount,
    DateOnly PostedDate,
    bool IsPending,
    string? CategoryPrimary,
    string? CategoryDetailed);

public sealed record BankTransactionBillStreamAssignment(
    Guid TransactionId,
    Guid? ExpectedBillStreamId,
    Guid? BillStreamId);

public interface IBankTransactionDiscoveryGateway
{
    Task<IReadOnlyList<BankTransactionDiscoveryRecord>>
        GetDiscoveryTransactionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default);

    Task StageBillStreamAssignmentsAsync(
        Guid userId,
        IReadOnlyCollection<BankTransactionBillStreamAssignment> assignments,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);
}

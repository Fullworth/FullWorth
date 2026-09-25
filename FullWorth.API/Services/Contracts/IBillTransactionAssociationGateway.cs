namespace FullWorth.API.Services.Contracts;

public sealed record BillTransactionAssociationRecord(
    Guid BankTransactionId,
    Guid BillStreamId);

public sealed record BillTransactionAssociationAssignment(
    Guid BankTransactionId,
    Guid? ExpectedBillStreamId,
    Guid? BillStreamId);

public interface IBillTransactionAssociationGateway
{
    Task<IReadOnlyList<BillTransactionAssociationRecord>> GetAsync(
        Guid userId,
        IReadOnlyCollection<Guid> bankTransactionIds,
        CancellationToken cancellationToken = default);

    Task StageAssignmentsAsync(
        Guid userId,
        IReadOnlyCollection<BillTransactionAssociationAssignment> assignments,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default);
}

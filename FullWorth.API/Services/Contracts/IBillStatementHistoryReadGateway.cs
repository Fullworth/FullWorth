namespace FullWorth.API.Services.Contracts;

public sealed record BillStatementHistoryReadRecord(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    decimal TotalAmount,
    string CurrencyCode);

public sealed record BillChangeHistoryReadRecord(
    Guid Id,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    string ChangeType,
    string Confidence,
    string Description,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    bool IsAcknowledged,
    DateTimeOffset DetectedAtUtc);

public sealed record BillStatementHistorySnapshot(
    IReadOnlyList<BillStatementHistoryReadRecord> Statements,
    IReadOnlyList<BillChangeHistoryReadRecord> Changes);

public interface IBillStatementHistoryReadGateway
{
    Task<BillStatementHistorySnapshot> GetAsync(
        Guid userId,
        Guid billStreamId,
        CancellationToken cancellationToken = default);
}

namespace FullWorth.API.Services.Contracts;

public sealed record AccountBankConnectionExportRecord(
    Guid Id,
    string InstitutionName,
    string Status,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountBankAccountExportRecord(
    Guid Id,
    Guid BankConnectionId,
    string Name,
    string? OfficialName,
    string? Mask,
    string AccountType,
    string? AccountSubtype,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountBankTransactionExportRecord(
    Guid Id,
    Guid BankAccountId,
    string Name,
    string? MerchantName,
    decimal Amount,
    string? IsoCurrencyCode,
    DateOnly PostedDate,
    DateOnly? AuthorizedDate,
    bool IsPending,
    bool IsRemoved,
    string? CategoryPrimary,
    string? CategoryDetailed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountPlaidLinkSessionExportRecord(
    Guid Id,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed record AccountBankExportSnapshot(
    IReadOnlyList<AccountBankConnectionExportRecord> BankConnections,
    IReadOnlyList<AccountBankAccountExportRecord> BankAccounts,
    IReadOnlyList<AccountBankTransactionExportRecord> BankTransactions,
    IReadOnlyList<AccountPlaidLinkSessionExportRecord> PlaidLinkSessions);

public interface IAccountBankExportGateway
{
    Task<AccountBankExportSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed record AccountBillStreamExportRecord(
    Guid Id,
    string ProviderName,
    string Category,
    string Source,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountBillAlertExportRecord(
    Guid Id,
    Guid? BillStreamId,
    Guid? BillChangeId,
    string AlertType,
    string Severity,
    string Title,
    string Message,
    bool IsRead,
    bool IsDismissed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountBillTransactionLinkExportRecord(
    Guid BankTransactionId,
    Guid BillStreamId);

public sealed record AccountBillExportSnapshot(
    IReadOnlyList<AccountBillStreamExportRecord> BillStreams,
    IReadOnlyList<AccountBillAlertExportRecord> BillAlerts,
    IReadOnlyList<AccountBillTransactionLinkExportRecord> TransactionLinks);

public interface IAccountBillExportGateway
{
    Task<AccountBillExportSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed record AccountBillStatementExportRecord(
    Guid Id,
    Guid BillStreamId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    decimal TotalAmount,
    string CurrencyCode,
    string? ProviderStatementId,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountBillLineItemExportRecord(
    Guid Id,
    Guid BillStatementId,
    string Description,
    decimal Amount,
    string? Category,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountBillChangeExportRecord(
    Guid Id,
    Guid BillStreamId,
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
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountStatementUploadExportRecord(
    Guid Id,
    Guid BillStreamId,
    Guid? BillStatementId,
    string MediaType,
    string FileExtension,
    long SizeBytes,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountAiEvaluationExportRecord(
    Guid Id,
    Guid BillStatementUploadId,
    string Provider,
    string Model,
    string PromptVersion,
    string Status,
    int AttemptCount,
    bool CandidateReadyForValidation,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AccountStatementExportSnapshot(
    IReadOnlyList<AccountBillStatementExportRecord> BillStatements,
    IReadOnlyList<AccountBillLineItemExportRecord> BillLineItems,
    IReadOnlyList<AccountBillChangeExportRecord> BillChanges,
    IReadOnlyList<AccountStatementUploadExportRecord> StatementUploads,
    IReadOnlyList<AccountAiEvaluationExportRecord> AiEvaluations);

public interface IAccountStatementExportGateway
{
    Task<AccountStatementExportSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Accounts;

public sealed class AccountDataExportBuilder(
    FullWorthDbContext dbContext,
    IAccountBankExportGateway bankExportGateway,
    IAccountBillExportGateway billExportGateway,
    IAccountStatementExportGateway statementExportGateway)
{
    public const string CurrentSchemaVersion = "1.1";

    public async Task<AccountDataExportResult> CreateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var userId =
            user.Id;

        var displayName =
            await dbContext
                .Set<IdentityUserClaim<Guid>>()
                .AsNoTracking()
                .Where(
                    claim =>
                        claim.UserId ==
                            userId &&
                        claim.ClaimType ==
                            ApplicationUser.DisplayNameClaimType)
                .OrderBy(
                    claim =>
                        claim.Id)
                .Select(
                    claim =>
                        claim.ClaimValue)
                .FirstOrDefaultAsync(
                    cancellationToken)
            ?? string.Empty;

        var bank =
            await bankExportGateway.GetAsync(
                userId,
                cancellationToken);

        var bills =
            await billExportGateway.GetAsync(
                userId,
                cancellationToken);

        var statements =
            await statementExportGateway.GetAsync(
                userId,
                cancellationToken);

        var billStreamIdByTransactionId =
            bills.TransactionAssociations
                .ToDictionary(
                    association => association.BankTransactionId,
                    association => association.BillStreamId);

        return new AccountDataExportResult(
            SchemaVersion:
                CurrentSchemaVersion,

            ExportedAtUtc:
                DateTimeOffset.UtcNow,

            Profile:
                new AccountProfileExport(
                    DisplayName:
                        displayName,

                    Email:
                        user.Email ??
                        string.Empty,

                    CreatedAtUtc:
                        user.CreatedAtUtc,

                    LastLoginAtUtc:
                        user.LastLoginAtUtc,

                    IsActive:
                        user.IsActive),

            BankConnections:
                bank.BankConnections
                    .Select(
                        item =>
                            new BankConnectionExport(
                                item.Id,
                                item.InstitutionName,
                                item.Status,
                                item.LastSuccessfulSyncAtUtc,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BankAccounts:
                bank.BankAccounts
                    .Select(
                        item =>
                            new BankAccountExport(
                                item.Id,
                                item.BankConnectionId,
                                item.Name,
                                item.OfficialName,
                                item.Mask,
                                item.AccountType,
                                item.AccountSubtype,
                                item.IsActive,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BankTransactions:
                bank.BankTransactions
                    .Select(
                        item =>
                            new BankTransactionExport(
                                item.Id,
                                item.BankAccountId,
                                billStreamIdByTransactionId.TryGetValue(
                                    item.Id,
                                    out var billStreamId)
                                    ? billStreamId
                                    : null,
                                item.Name,
                                item.MerchantName,
                                item.Amount,
                                item.IsoCurrencyCode,
                                item.PostedDate,
                                item.AuthorizedDate,
                                item.IsPending,
                                item.IsRemoved,
                                item.CategoryPrimary,
                                item.CategoryDetailed,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BillStreams:
                bills.BillStreams
                    .Select(
                        item =>
                            new BillStreamExport(
                                item.Id,
                                item.ProviderName,
                                item.Category,
                                item.Source,
                                item.IsActive,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BillStatements:
                statements.BillStatements
                    .Select(
                        item =>
                            new BillStatementExport(
                                item.Id,
                                item.BillStreamId,
                                item.PeriodStart,
                                item.PeriodEnd,
                                item.StatementDate,
                                item.DueDate,
                                item.TotalAmount,
                                item.CurrencyCode,
                                item.ProviderStatementId,
                                item.RetrievedAtUtc,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BillLineItems:
                statements.BillLineItems
                    .Select(
                        item =>
                            new BillLineItemExport(
                                item.Id,
                                item.BillStatementId,
                                item.Description,
                                item.Amount,
                                item.Category,
                                item.SortOrder,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BillChanges:
                statements.BillChanges
                    .Select(
                        item =>
                            new BillChangeExport(
                                item.Id,
                                item.BillStreamId,
                                item.PreviousStatementId,
                                item.CurrentStatementId,
                                item.ChangeType,
                                item.Confidence,
                                item.Description,
                                item.PreviousAmount,
                                item.CurrentAmount,
                                item.AmountDifference,
                                item.AnnualizedImpact,
                                item.IsAcknowledged,
                                item.DetectedAtUtc,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            BillAlerts:
                bills.BillAlerts
                    .Select(
                        item =>
                            new BillAlertExport(
                                item.Id,
                                item.BillStreamId,
                                item.BillChangeId,
                                item.AlertType,
                                item.Severity,
                                item.Title,
                                item.Message,
                                item.IsRead,
                                item.IsDismissed,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            StatementUploads:
                statements.StatementUploads
                    .Select(
                        item =>
                            new StatementUploadExport(
                                item.Id,
                                item.BillStreamId,
                                item.BillStatementId,
                                item.MediaType,
                                item.FileExtension,
                                item.SizeBytes,
                                item.Status,
                                $"/api/bill-streams/{item.BillStreamId}/statement-uploads/{item.Id}/file",
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            AiEvaluations:
                statements.AiEvaluations
                    .Select(
                        item =>
                            new AiEvaluationExport(
                                item.Id,
                                item.BillStatementUploadId,
                                item.Provider,
                                item.Model,
                                item.PromptVersion,
                                item.Status,
                                item.AttemptCount,
                                item.CandidateReadyForValidation,
                                item.LastAttemptedAtUtc,
                                item.CompletedAtUtc,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc))
                    .ToArray(),

            PlaidLinkSessions:
                bank.PlaidLinkSessions
                    .Select(
                        item =>
                            new PlaidLinkSessionExport(
                                item.Id,
                                item.Status,
                                item.ExpiresAtUtc,
                                item.CreatedAtUtc,
                                item.UpdatedAtUtc,
                                item.CompletedAtUtc))
                    .ToArray());
    }
}

public sealed record AccountDataExportResult(
    string SchemaVersion,
    DateTimeOffset ExportedAtUtc,
    AccountProfileExport Profile,
    IReadOnlyList<BankConnectionExport> BankConnections,
    IReadOnlyList<BankAccountExport> BankAccounts,
    IReadOnlyList<BankTransactionExport> BankTransactions,
    IReadOnlyList<BillStreamExport> BillStreams,
    IReadOnlyList<BillStatementExport> BillStatements,
    IReadOnlyList<BillLineItemExport> BillLineItems,
    IReadOnlyList<BillChangeExport> BillChanges,
    IReadOnlyList<BillAlertExport> BillAlerts,
    IReadOnlyList<StatementUploadExport> StatementUploads,
    IReadOnlyList<AiEvaluationExport> AiEvaluations,
    IReadOnlyList<PlaidLinkSessionExport> PlaidLinkSessions);

public sealed record AccountProfileExport(
    string DisplayName,
    string Email,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    bool IsActive);

public sealed record BankConnectionExport(
    Guid Id,
    string InstitutionName,
    string Status,
    DateTimeOffset? LastSuccessfulSyncAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BankAccountExport(
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

public sealed record BankTransactionExport(
    Guid Id,
    Guid BankAccountId,
    Guid? BillStreamId,
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

public sealed record BillStreamExport(
    Guid Id,
    string ProviderName,
    string Category,
    string Source,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillStatementExport(
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

public sealed record BillLineItemExport(
    Guid Id,
    Guid BillStatementId,
    string Description,
    decimal Amount,
    string? Category,
    int SortOrder,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record BillChangeExport(
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

public sealed record BillAlertExport(
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

public sealed record StatementUploadExport(
    Guid Id,
    Guid BillStreamId,
    Guid? BillStatementId,
    string MediaType,
    string FileExtension,
    long SizeBytes,
    string Status,
    string DownloadPath,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AiEvaluationExport(
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

public sealed record PlaidLinkSessionExport(
    Guid Id,
    string Status,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
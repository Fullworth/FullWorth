using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class AccountStatementExportGateway(
    FullWorthDbContext dbContext)
    : IAccountStatementExportGateway
{
    public async Task<AccountStatementExportSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var statements =
            await dbContext.BillStatements
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.PeriodStart)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var lineItems =
            await dbContext.BillLineItems
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.BillStatementId)
                .ThenBy(item => item.SortOrder)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var changes =
            await dbContext.BillChanges
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.DetectedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var uploads =
            await dbContext.BillStatementUploads
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var evaluations =
            await dbContext.BillStatementAiEvaluations
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        return new AccountStatementExportSnapshot(
            statements
                .Select(item => new AccountBillStatementExportRecord(
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
            lineItems
                .Select(item => new AccountBillLineItemExportRecord(
                    item.Id,
                    item.BillStatementId,
                    item.Description,
                    item.Amount,
                    item.Category,
                    item.SortOrder,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            changes
                .Select(item => new AccountBillChangeExportRecord(
                    item.Id,
                    item.BillStreamId,
                    item.PreviousStatementId,
                    item.CurrentStatementId,
                    item.ChangeType.ToString(),
                    item.Confidence.ToString(),
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
            uploads
                .Select(item => new AccountStatementUploadExportRecord(
                    item.Id,
                    item.BillStreamId,
                    item.BillStatementId,
                    item.MediaType,
                    item.FileExtension,
                    item.SizeBytes,
                    item.Status.ToString(),
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            evaluations
                .Select(item => new AccountAiEvaluationExportRecord(
                    item.Id,
                    item.BillStatementUploadId,
                    item.Provider,
                    item.Model,
                    item.PromptVersion,
                    item.Status.ToString(),
                    item.AttemptCount,
                    item.CandidateReadyForValidation,
                    item.LastAttemptedAtUtc,
                    item.CompletedAtUtc,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray());
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class AccountBillExportGateway(
    FullWorthDbContext dbContext)
    : IAccountBillExportGateway
{
    public async Task<AccountBillExportSnapshot> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        var streams =
            await dbContext.BillStreams
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var alerts =
            await dbContext.BillAlerts
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.CreatedAtUtc)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);

        var transactionAssociations =
            await dbContext.BillTransactionAssociations
                .AsNoTracking()
                .Where(item => item.UserId == userId)
                .OrderBy(item => item.BankTransactionId)
                .ToListAsync(cancellationToken);

        return new AccountBillExportSnapshot(
            streams
                .Select(item => new AccountBillStreamExportRecord(
                    item.Id,
                    item.ProviderName,
                    item.Category.ToString(),
                    item.Source.ToString(),
                    item.IsActive,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            alerts
                .Select(item => new AccountBillAlertExportRecord(
                    item.Id,
                    item.BillStreamId,
                    item.BillChangeId,
                    item.AlertType.ToString(),
                    item.Severity.ToString(),
                    item.Title,
                    item.Message,
                    item.IsRead,
                    item.IsDismissed,
                    item.CreatedAtUtc,
                    item.UpdatedAtUtc))
                .ToArray(),
            transactionAssociations
                .Select(item => new AccountBillTransactionAssociationExportRecord(
                    item.BankTransactionId,
                    item.BillStreamId))
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

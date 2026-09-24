using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementHistoryReadGateway(
    FullWorthDbContext dbContext)
    : IBillStatementHistoryReadGateway
{
    public async Task<BillStatementHistorySnapshot> GetAsync(
        Guid userId,
        Guid billStreamId,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (billStreamId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        var statements =
            await dbContext.BillStatements
                .AsNoTracking()
                .Where(
                    statement =>
                        statement.UserId ==
                            userId &&
                        statement.BillStreamId ==
                            billStreamId)
                .OrderByDescending(
                    statement =>
                        statement.PeriodEnd)
                .ThenByDescending(
                    statement =>
                        statement.StatementDate)
                .Select(
                    statement =>
                        new BillStatementHistoryReadRecord(
                            statement.Id,
                            statement.PeriodStart,
                            statement.PeriodEnd,
                            statement.StatementDate,
                            statement.DueDate,
                            statement.TotalAmount,
                            statement.CurrencyCode))
                .ToListAsync(
                    cancellationToken);

        var changes =
            await dbContext.BillChanges
                .AsNoTracking()
                .Where(
                    change =>
                        change.UserId ==
                            userId &&
                        change.BillStreamId ==
                            billStreamId)
                .OrderByDescending(
                    change =>
                        change.DetectedAtUtc)
                .Select(
                    change =>
                        new BillChangeHistoryReadRecord(
                            change.Id,
                            change.PreviousStatementId,
                            change.CurrentStatementId,
                            change.ChangeType.ToString(),
                            change.Confidence.ToString(),
                            change.Description,
                            change.PreviousAmount,
                            change.CurrentAmount,
                            change.AmountDifference,
                            change.AnnualizedImpact,
                            change.IsAcknowledged,
                            change.DetectedAtUtc))
                .ToListAsync(
                    cancellationToken);

        return new BillStatementHistorySnapshot(
            statements,
            changes);
    }
}

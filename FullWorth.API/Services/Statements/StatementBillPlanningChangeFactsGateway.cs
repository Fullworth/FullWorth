using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class StatementBillPlanningChangeFactsGateway(
    FullWorthDbContext dbContext)
    : IBillPlanningChangeFactsGateway
{
    private const int MaximumBillStreamsPerRequest =
        500;

    public async Task<IReadOnlyDictionary<Guid, BillPlanningChangeFact>>
        GetLatestConfirmedAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            billStreamIds);

        if (billStreamIds.Count ==
            0)
        {
            return new Dictionary<Guid, BillPlanningChangeFact>();
        }

        var ids =
            billStreamIds
                .Distinct()
                .ToArray();

        if (ids.Any(
                id =>
                    id == Guid.Empty))
        {
            throw new ArgumentException(
                "Bill stream IDs must not be empty.",
                nameof(billStreamIds));
        }

        if (ids.Length >
            MaximumBillStreamsPerRequest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(billStreamIds),
                $"At most {MaximumBillStreamsPerRequest} bill streams may be requested at once.");
        }

        var candidates =
            await (
                from change in
                    dbContext.BillChanges.AsNoTracking()
                join statement in
                    dbContext.BillStatements.AsNoTracking()
                    on new
                    {
                        change.CurrentStatementId,
                        change.UserId
                    }
                    equals new
                    {
                        CurrentStatementId =
                            statement.Id,
                        statement.UserId
                    }
                where
                    change.UserId ==
                        userId &&
                    ids.Contains(
                        change.BillStreamId) &&
                    statement.BillStreamId ==
                        change.BillStreamId &&
                    change.Confidence ==
                        BillChangeConfidence.Confirmed &&
                    (
                        change.ChangeType ==
                            BillChangeType.TotalIncrease ||
                        change.ChangeType ==
                            BillChangeType.TotalDecrease
                    )
                orderby
                    change.BillStreamId,
                    statement.PeriodEnd descending,
                    change.DetectedAtUtc descending,
                    change.Id descending
                select new BillPlanningChangeFact(
                    change.BillStreamId,
                    change.Id,
                    change.PreviousStatementId,
                    change.CurrentStatementId,
                    change.PreviousAmount,
                    change.CurrentAmount,
                    change.AmountDifference,
                    change.AnnualizedImpact,
                    change.Description,
                    statement.PeriodEnd)
            )
            .ToListAsync(
                cancellationToken);

        return candidates
            .GroupBy(
                candidate =>
                    candidate.BillStreamId)
            .ToDictionary(
                group =>
                    group.Key,
                group =>
                    group.First());
    }
}

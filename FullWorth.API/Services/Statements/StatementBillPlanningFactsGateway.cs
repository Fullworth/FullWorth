using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class StatementBillPlanningFactsGateway(
    FullWorthDbContext dbContext)
    : IBillPlanningFactsGateway
{
    private const int MaximumBillStreamsPerRequest = 500;

    public async Task<IReadOnlyDictionary<Guid, BillPlanningFact>>
        GetLatestAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            billStreamIds);

        if (billStreamIds.Count == 0)
        {
            return new Dictionary<Guid, BillPlanningFact>();
        }

        var ids =
            billStreamIds
                .Distinct()
                .ToArray();

        if (ids.Any(
                id => id == Guid.Empty))
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
            await dbContext.BillStatements
                .AsNoTracking()
                .Where(
                    statement =>
                        statement.UserId == userId &&
                        ids.Contains(
                            statement.BillStreamId))
                .OrderBy(
                    statement =>
                        statement.BillStreamId)
                .ThenByDescending(
                    statement =>
                        statement.PeriodEnd)
                .ThenByDescending(
                    statement =>
                        statement.StatementDate)
                .ThenByDescending(
                    statement =>
                        statement.CreatedAtUtc)
                .ThenByDescending(
                    statement =>
                        statement.Id)
                .Select(
                    statement =>
                        new BillPlanningFact(
                            statement.BillStreamId,
                            statement.Id,
                            statement.TotalAmount,
                            statement.CurrencyCode,
                            statement.DueDate,
                            statement.PeriodEnd))
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

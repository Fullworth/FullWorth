using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidPlanningPostedPayrollFactsGateway(
    FullWorthDbContext dbContext)
    : IPlanningPostedPayrollFactsGateway
{
    private const int MaximumDateSpanDays = 366;
    private const string IncomeCategory = "INCOME";
    private const string WageCategory = "INCOME_WAGES";

    public async Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(
        Guid userId,
        DateOnly fromInclusive,
        DateOnly throughInclusive,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (throughInclusive <
            fromInclusive)
        {
            throw new ArgumentException(
                "Payroll fact range end cannot be before its start.",
                nameof(throughInclusive));
        }

        if (throughInclusive.DayNumber -
                fromInclusive.DayNumber >
            MaximumDateSpanDays)
        {
            throw new ArgumentOutOfRangeException(
                nameof(throughInclusive),
                $"Payroll fact range cannot exceed {MaximumDateSpanDays} days.");
        }

        return await dbContext.BankTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.UserId == userId &&
                    !transaction.IsRemoved &&
                    !transaction.IsPending &&
                    transaction.PostedDate >= fromInclusive &&
                    transaction.PostedDate <= throughInclusive &&
                    transaction.Amount < 0m &&
                    transaction.CategoryPrimary == IncomeCategory &&
                    transaction.CategoryDetailed == WageCategory)
            .OrderBy(
                transaction =>
                    transaction.PostedDate)
            .ThenBy(
                transaction =>
                    transaction.Id)
            .Select(
                transaction =>
                    new PlanningPostedPayrollFact(
                        transaction.Id,
                        -transaction.Amount,
                        transaction.PostedDate,
                        transaction.IsoCurrencyCode))
            .ToListAsync(
                cancellationToken);
    }
}

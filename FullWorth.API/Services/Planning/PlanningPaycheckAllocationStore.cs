using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Planning;

public sealed record PlanningPaycheckAllocationDraft(
    Guid BillStreamId,
    Guid SourceStatementId,
    DateOnly BillPeriodEnd,
    DateOnly BillDueDate,
    decimal PlannedAmount,
    string CurrencyCode);

public sealed record PlanningPaycheckAllocationSnapshot(
    Guid Id,
    Guid PayrollTransactionId,
    Guid BillStreamId,
    Guid SourceStatementId,
    DateOnly PaycheckPostedDate,
    DateOnly BillPeriodEnd,
    DateOnly BillDueDate,
    decimal PlannedAmount,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc);

public sealed record PlanningBillCyclePlannedTotal(
    Guid BillStreamId,
    DateOnly BillPeriodEnd,
    string CurrencyCode,
    decimal PlannedAmount);

public sealed class PlanningPaycheckAllocationStore(
    FullWorthDbContext dbContext,
    TimeProvider timeProvider)
{
    private const int MaximumBillsPerPaycheck = 500;

    public async Task<IReadOnlyList<PlanningPaycheckAllocationSnapshot>>
        GetForPaycheckAsync(
            Guid userId,
            Guid payrollTransactionId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ValidateOpaqueId(
            payrollTransactionId,
            nameof(payrollTransactionId));

        return await QueryPaycheckAsync(
            userId,
            payrollTransactionId,
            cancellationToken);
    }

    public async Task<IReadOnlyList<PlanningBillCyclePlannedTotal>>
        GetPriorTotalsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            DateOnly throughPaycheckPostedDateInclusive,
            Guid excludedPayrollTransactionId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ArgumentNullException.ThrowIfNull(
            billStreamIds);

        ValidateOpaqueId(
            excludedPayrollTransactionId,
            nameof(excludedPayrollTransactionId));

        if (billStreamIds.Count ==
            0)
        {
            return [];
        }

        var ids =
            billStreamIds
                .Distinct()
                .ToArray();

        if (ids.Length >
            MaximumBillsPerPaycheck)
        {
            throw new ArgumentOutOfRangeException(
                nameof(billStreamIds),
                $"At most {MaximumBillsPerPaycheck} bill streams may be requested at once.");
        }

        if (ids.Any(
                id =>
                    id == Guid.Empty))
        {
            throw new ArgumentException(
                "Bill stream IDs must not be empty.",
                nameof(billStreamIds));
        }

        var allocations =
            await dbContext.PlanningPaycheckAllocations
                .AsNoTracking()
                .Where(
                    allocation =>
                        allocation.UserId == userId &&
                        allocation.PaycheckPostedDate <=
                            throughPaycheckPostedDateInclusive &&
                        allocation.PayrollTransactionId !=
                            excludedPayrollTransactionId &&
                        ids.Contains(
                            allocation.BillStreamId))
                .Select(
                    allocation =>
                        new
                        {
                            allocation.BillStreamId,
                            allocation.BillPeriodEnd,
                            allocation.CurrencyCode,
                            allocation.PlannedAmount
                        })
                .ToListAsync(
                    cancellationToken);

        return allocations
            .GroupBy(
                allocation =>
                    new
                    {
                        allocation.BillStreamId,
                        allocation.BillPeriodEnd,
                        allocation.CurrencyCode
                    })
            .Select(
                group =>
                    new PlanningBillCyclePlannedTotal(
                        group.Key.BillStreamId,
                        group.Key.BillPeriodEnd,
                        group.Key.CurrencyCode,
                        RoundMoney(
                            group.Sum(
                                item =>
                                    item.PlannedAmount))))
            .OrderBy(
                total =>
                    total.BillPeriodEnd)
            .ThenBy(
                total =>
                    total.BillStreamId)
            .ToList();
    }

    public async Task<IReadOnlyList<PlanningPaycheckAllocationSnapshot>>
        SavePaycheckPlanAsync(
            Guid userId,
            Guid payrollTransactionId,
            DateOnly paycheckPostedDate,
            IReadOnlyCollection<PlanningPaycheckAllocationDraft> allocations,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ValidateOpaqueId(
            payrollTransactionId,
            nameof(payrollTransactionId));

        ArgumentNullException.ThrowIfNull(
            allocations);

        if (allocations.Count >
            MaximumBillsPerPaycheck)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allocations),
                $"At most {MaximumBillsPerPaycheck} bill allocations may be saved for one paycheck.");
        }

        var normalized =
            allocations
                .Select(
                    Normalize)
                .OrderBy(
                    allocation =>
                        allocation.BillPeriodEnd)
                .ThenBy(
                    allocation =>
                        allocation.BillStreamId)
                .ToArray();

        if (normalized
            .GroupBy(
                allocation =>
                    new
                    {
                        allocation.BillStreamId,
                        allocation.BillPeriodEnd
                    })
            .Any(
                group =>
                    group.Count() >
                    1))
        {
            throw new ArgumentException(
                "A paycheck plan may contain only one recommendation for each bill cycle.",
                nameof(allocations));
        }

        var existing =
            await QueryPaycheckAsync(
                userId,
                payrollTransactionId,
                cancellationToken);

        if (existing.Count >
            0)
        {
            if (!Matches(
                    existing,
                    paycheckPostedDate,
                    normalized))
            {
                throw new InvalidOperationException(
                    "A different planning recommendation is already recorded for this paycheck.");
            }

            return existing;
        }

        if (normalized.Length ==
            0)
        {
            return [];
        }

        var createdAtUtc =
            timeProvider.GetUtcNow();

        foreach (var allocation in
                 normalized)
        {
            dbContext.PlanningPaycheckAllocations.Add(
                new PlanningPaycheckAllocationEntity
                {
                    UserId = userId,
                    PayrollTransactionId =
                        payrollTransactionId,
                    BillStreamId =
                        allocation.BillStreamId,
                    SourceStatementId =
                        allocation.SourceStatementId,
                    PaycheckPostedDate =
                        paycheckPostedDate,
                    BillPeriodEnd =
                        allocation.BillPeriodEnd,
                    BillDueDate =
                        allocation.BillDueDate,
                    PlannedAmount =
                        allocation.PlannedAmount,
                    CurrencyCode =
                        allocation.CurrencyCode,
                    CreatedAtUtc =
                        createdAtUtc
                });
        }

        try
        {
            await dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            /*
             * The database uniqueness constraint is the final concurrency
             * boundary. If another request won the same-paycheck race, load
             * the persisted result and accept it only when it is identical.
             */
            dbContext.ChangeTracker.Clear();

            existing =
                await QueryPaycheckAsync(
                    userId,
                    payrollTransactionId,
                    cancellationToken);

            if (existing.Count >
                    0 &&
                Matches(
                    existing,
                    paycheckPostedDate,
                    normalized))
            {
                return existing;
            }

            throw;
        }

        return await QueryPaycheckAsync(
            userId,
            payrollTransactionId,
            cancellationToken);
    }

    private async Task<IReadOnlyList<PlanningPaycheckAllocationSnapshot>>
        QueryPaycheckAsync(
            Guid userId,
            Guid payrollTransactionId,
            CancellationToken cancellationToken)
    {
        return await dbContext.PlanningPaycheckAllocations
            .AsNoTracking()
            .Where(
                allocation =>
                    allocation.UserId == userId &&
                    allocation.PayrollTransactionId ==
                        payrollTransactionId)
            .OrderBy(
                allocation =>
                    allocation.BillPeriodEnd)
            .ThenBy(
                allocation =>
                    allocation.BillStreamId)
            .Select(
                allocation =>
                    new PlanningPaycheckAllocationSnapshot(
                        allocation.Id,
                        allocation.PayrollTransactionId,
                        allocation.BillStreamId,
                        allocation.SourceStatementId,
                        allocation.PaycheckPostedDate,
                        allocation.BillPeriodEnd,
                        allocation.BillDueDate,
                        allocation.PlannedAmount,
                        allocation.CurrencyCode,
                        allocation.CreatedAtUtc))
            .ToListAsync(
                cancellationToken);
    }

    private static PlanningPaycheckAllocationDraft Normalize(
        PlanningPaycheckAllocationDraft allocation)
    {
        ArgumentNullException.ThrowIfNull(
            allocation);

        ValidateOpaqueId(
            allocation.BillStreamId,
            nameof(allocation.BillStreamId));

        ValidateOpaqueId(
            allocation.SourceStatementId,
            nameof(allocation.SourceStatementId));

        if (allocation.PlannedAmount <=
            0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allocation.PlannedAmount),
                "Planned amount must be greater than zero.");
        }

        if (decimal.Round(
                allocation.PlannedAmount,
                2,
                MidpointRounding.AwayFromZero) !=
            allocation.PlannedAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allocation.PlannedAmount),
                "Planned amount must not contain fractions of a cent.");
        }

        var currency =
            allocation.CurrencyCode?.Trim().ToUpperInvariant();

        if (currency is null ||
            currency.Length !=
                3 ||
            currency.Any(
                character =>
                    character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Currency code must be a three-letter alphabetic code.",
                nameof(allocation.CurrencyCode));
        }

        return allocation with
        {
            PlannedAmount =
                RoundMoney(
                    allocation.PlannedAmount),
            CurrencyCode =
                currency
        };
    }

    private static bool Matches(
        IReadOnlyList<PlanningPaycheckAllocationSnapshot> existing,
        DateOnly paycheckPostedDate,
        IReadOnlyList<PlanningPaycheckAllocationDraft> requested)
    {
        if (existing.Count !=
            requested.Count)
        {
            return false;
        }

        var orderedExisting =
            existing
                .OrderBy(
                    allocation =>
                        allocation.BillPeriodEnd)
                .ThenBy(
                    allocation =>
                        allocation.BillStreamId)
                .ToArray();

        for (var index = 0;
             index < requested.Count;
             index++)
        {
            var saved =
                orderedExisting[index];

            var proposed =
                requested[index];

            if (saved.PaycheckPostedDate !=
                    paycheckPostedDate ||
                saved.BillStreamId !=
                    proposed.BillStreamId ||
                saved.SourceStatementId !=
                    proposed.SourceStatementId ||
                saved.BillPeriodEnd !=
                    proposed.BillPeriodEnd ||
                saved.BillDueDate !=
                    proposed.BillDueDate ||
                saved.PlannedAmount !=
                    proposed.PlannedAmount ||
                !string.Equals(
                    saved.CurrencyCode,
                    proposed.CurrencyCode,
                    StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }

    private static void ValidateOpaqueId(
        Guid value,
        string parameterName)
    {
        if (value ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Identifier is required.",
                parameterName);
        }
    }

    private static decimal RoundMoney(
        decimal value) =>
        decimal.Round(
            value,
            2,
            MidpointRounding.AwayFromZero);
}

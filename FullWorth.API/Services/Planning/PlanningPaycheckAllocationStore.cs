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

public sealed record PlanningPaycheckPlanRunDraft(
    DateOnly PaycheckPostedDate,
    decimal PaycheckAmount,
    string CurrencyCode,
    decimal RecommendedSetAside,
    decimal PaycheckRemainingAfterPlan,
    decimal Shortfall);

public sealed record PlanningPaycheckPlanRunSnapshot(
    Guid Id,
    Guid PayrollTransactionId,
    DateOnly PaycheckPostedDate,
    decimal PaycheckAmount,
    string CurrencyCode,
    decimal RecommendedSetAside,
    decimal PaycheckRemainingAfterPlan,
    decimal Shortfall,
    DateTimeOffset CreatedAtUtc);

public sealed record PlanningSavedPaycheckPlan(
    PlanningPaycheckPlanRunSnapshot Run,
    IReadOnlyList<PlanningPaycheckAllocationSnapshot> Allocations,
    bool WasExisting);

public sealed record PlanningPaycheckPlanHistorySnapshot(
    PlanningPaycheckPlanRunSnapshot Run,
    IReadOnlyList<PlanningPaycheckAllocationSnapshot> Allocations);

public sealed class PlanningPaycheckAllocationStore(
    FullWorthDbContext dbContext,
    TimeProvider timeProvider)
{
    private const int MaximumBillsPerPaycheck = 500;

    public async Task<IReadOnlyList<PlanningPaycheckPlanRunSnapshot>>
        GetRecentPaycheckPlanRunsAsync(
            Guid userId,
            int take = 5,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        if (take is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take),
                "Recent paycheck-plan request size must be between 1 and 20.");
        }

        return await dbContext.PlanningPaycheckPlanRuns
            .AsNoTracking()
            .Where(
                run =>
                    run.UserId ==
                        userId)
            .OrderByDescending(
                run =>
                    run.PaycheckPostedDate)
            .ThenByDescending(
                run =>
                    run.CreatedAtUtc)
            .ThenByDescending(
                run =>
                    run.Id)
            .Take(
                take)
            .Select(
                run =>
                    new PlanningPaycheckPlanRunSnapshot(
                        run.Id,
                        run.PayrollTransactionId,
                        run.PaycheckPostedDate,
                        run.PaycheckAmount,
                        run.CurrencyCode,
                        run.RecommendedSetAside,
                        run.PaycheckRemainingAfterPlan,
                        run.Shortfall,
                        run.CreatedAtUtc))
            .ToListAsync(
                cancellationToken);
    }

    public async Task<IReadOnlyList<PlanningPaycheckPlanHistorySnapshot>>
        GetRecentPaycheckPlanHistoryAsync(
            Guid userId,
            int take = 5,
            CancellationToken cancellationToken = default)
    {
        var runs =
            await GetRecentPaycheckPlanRunsAsync(
                userId,
                take,
                cancellationToken);

        if (runs.Count == 0)
        {
            return [];
        }

        var payrollTransactionIds =
            runs
                .Select(
                    run =>
                        run.PayrollTransactionId)
                .ToArray();

        var allocations =
            await dbContext.PlanningPaycheckAllocations
                .AsNoTracking()
                .Where(
                    allocation =>
                        allocation.UserId == userId &&
                        payrollTransactionIds.Contains(
                            allocation.PayrollTransactionId))
                .OrderBy(
                    allocation =>
                        allocation.BillDueDate)
                .ThenBy(
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

        var allocationsByPayroll =
            allocations
                .GroupBy(
                    allocation =>
                        allocation.PayrollTransactionId)
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        (IReadOnlyList<PlanningPaycheckAllocationSnapshot>)
                            group.ToList());

        return runs
            .Select(
                run =>
                    new PlanningPaycheckPlanHistorySnapshot(
                        run,
                        allocationsByPayroll.TryGetValue(
                            run.PayrollTransactionId,
                            out var runAllocations)
                            ? runAllocations
                            : []))
            .ToList();
    }

    public async Task<PlanningSavedPaycheckPlan?>
        GetSavedPaycheckPlanAsync(
            Guid userId,
            Guid payrollTransactionId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ValidateOpaqueId(
            payrollTransactionId,
            nameof(payrollTransactionId));

        return await QuerySavedPaycheckPlanAsync(
            userId,
            payrollTransactionId,
            cancellationToken);
    }

    public async Task<PlanningSavedPaycheckPlan>
        SaveCompletePaycheckPlanAsync(
            Guid userId,
            Guid payrollTransactionId,
            PlanningPaycheckPlanRunDraft run,
            IReadOnlyCollection<PlanningPaycheckAllocationDraft> allocations,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ValidateOpaqueId(
            payrollTransactionId,
            nameof(payrollTransactionId));

        ArgumentNullException.ThrowIfNull(
            run);

        ArgumentNullException.ThrowIfNull(
            allocations);

        if (allocations.Count >
            MaximumBillsPerPaycheck)
        {
            throw new ArgumentOutOfRangeException(
                nameof(allocations),
                $"At most {MaximumBillsPerPaycheck} bill allocations may be saved for one paycheck.");
        }

        var normalizedRun =
            Normalize(
                run);

        var normalizedAllocations =
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

        if (normalizedAllocations
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

        if (normalizedAllocations.Any(
                allocation =>
                    !string.Equals(
                        allocation.CurrencyCode,
                        normalizedRun.CurrencyCode,
                        StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "A paycheck plan cannot combine different currencies.");
        }

        var allocationTotal =
            RoundMoney(
                normalizedAllocations.Sum(
                    allocation =>
                        allocation.PlannedAmount));

        if (allocationTotal !=
            normalizedRun.RecommendedSetAside)
        {
            throw new InvalidOperationException(
                "Paycheck-plan allocation total must equal the recommended set-aside total.");
        }

        var existing =
            await QuerySavedPaycheckPlanAsync(
                userId,
                payrollTransactionId,
                cancellationToken);

        if (existing is not null)
        {
            if (!Matches(
                    existing,
                    normalizedRun,
                    normalizedAllocations))
            {
                throw new InvalidOperationException(
                    "A different planning result is already recorded for this paycheck.");
            }

            return existing with
            {
                WasExisting =
                    true
            };
        }

        var createdAtUtc =
            timeProvider.GetUtcNow();

        dbContext.PlanningPaycheckPlanRuns.Add(
            new PlanningPaycheckPlanRunEntity
            {
                UserId =
                    userId,
                PayrollTransactionId =
                    payrollTransactionId,
                PaycheckPostedDate =
                    normalizedRun.PaycheckPostedDate,
                PaycheckAmount =
                    normalizedRun.PaycheckAmount,
                CurrencyCode =
                    normalizedRun.CurrencyCode,
                RecommendedSetAside =
                    normalizedRun.RecommendedSetAside,
                PaycheckRemainingAfterPlan =
                    normalizedRun.PaycheckRemainingAfterPlan,
                Shortfall =
                    normalizedRun.Shortfall,
                CreatedAtUtc =
                    createdAtUtc
            });

        foreach (var allocation in
                 normalizedAllocations)
        {
            dbContext.PlanningPaycheckAllocations.Add(
                new PlanningPaycheckAllocationEntity
                {
                    UserId =
                        userId,
                    PayrollTransactionId =
                        payrollTransactionId,
                    BillStreamId =
                        allocation.BillStreamId,
                    SourceStatementId =
                        allocation.SourceStatementId,
                    PaycheckPostedDate =
                        normalizedRun.PaycheckPostedDate,
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
            /*
             * The immutable run row and every recommendation row are committed
             * by one SaveChanges call. A ready plan therefore cannot exist
             * with only one half of its Planning-owned persistence recorded.
             */
            await dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();

            existing =
                await QuerySavedPaycheckPlanAsync(
                    userId,
                    payrollTransactionId,
                    cancellationToken);

            if (existing is not null &&
                Matches(
                    existing,
                    normalizedRun,
                    normalizedAllocations))
            {
                return existing with
                {
                    WasExisting =
                        true
                };
            }

            throw;
        }

        var saved =
            await QuerySavedPaycheckPlanAsync(
                userId,
                payrollTransactionId,
                cancellationToken);

        return saved is null
            ? throw new InvalidOperationException(
                "The saved paycheck plan could not be reloaded.")
            : saved with
            {
                WasExisting =
                    false
            };
    }

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

    public async Task<IReadOnlyList<PlanningBillCyclePlannedTotal>>
        GetCycleTotalsAsync(
            Guid userId,
            IReadOnlyCollection<Guid> billStreamIds,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ArgumentNullException.ThrowIfNull(
            billStreamIds);

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
                        allocation.UserId ==
                            userId &&
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

    private async Task<PlanningSavedPaycheckPlan?>
        QuerySavedPaycheckPlanAsync(
            Guid userId,
            Guid payrollTransactionId,
            CancellationToken cancellationToken)
    {
        var run =
            await dbContext.PlanningPaycheckPlanRuns
                .AsNoTracking()
                .Where(
                    candidate =>
                        candidate.UserId ==
                            userId &&
                        candidate.PayrollTransactionId ==
                            payrollTransactionId)
                .Select(
                    candidate =>
                        new PlanningPaycheckPlanRunSnapshot(
                            candidate.Id,
                            candidate.PayrollTransactionId,
                            candidate.PaycheckPostedDate,
                            candidate.PaycheckAmount,
                            candidate.CurrencyCode,
                            candidate.RecommendedSetAside,
                            candidate.PaycheckRemainingAfterPlan,
                            candidate.Shortfall,
                            candidate.CreatedAtUtc))
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (run is null)
        {
            return null;
        }

        var allocations =
            await QueryPaycheckAsync(
                userId,
                payrollTransactionId,
                cancellationToken);

        return new PlanningSavedPaycheckPlan(
            run,
            allocations,
            WasExisting: true);
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

    private static PlanningPaycheckPlanRunDraft Normalize(
        PlanningPaycheckPlanRunDraft run)
    {
        ValidateMoney(
            run.PaycheckAmount,
            nameof(run.PaycheckAmount));

        ValidateMoney(
            run.RecommendedSetAside,
            nameof(run.RecommendedSetAside));

        ValidateMoney(
            run.PaycheckRemainingAfterPlan,
            nameof(run.PaycheckRemainingAfterPlan));

        ValidateMoney(
            run.Shortfall,
            nameof(run.Shortfall));

        var currency =
            NormalizeCurrency(
                run.CurrencyCode,
                nameof(run.CurrencyCode));

        if (RoundMoney(
                run.PaycheckAmount +
                run.Shortfall) !=
            RoundMoney(
                run.RecommendedSetAside +
                run.PaycheckRemainingAfterPlan))
        {
            throw new InvalidOperationException(
                "Paycheck-plan totals are internally inconsistent.");
        }

        return run with
        {
            PaycheckAmount =
                RoundMoney(
                    run.PaycheckAmount),
            CurrencyCode =
                currency,
            RecommendedSetAside =
                RoundMoney(
                    run.RecommendedSetAside),
            PaycheckRemainingAfterPlan =
                RoundMoney(
                    run.PaycheckRemainingAfterPlan),
            Shortfall =
                RoundMoney(
                    run.Shortfall)
        };
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
        PlanningSavedPaycheckPlan existing,
        PlanningPaycheckPlanRunDraft requestedRun,
        IReadOnlyList<PlanningPaycheckAllocationDraft> requestedAllocations)
    {
        var run =
            existing.Run;

        return run.PaycheckPostedDate ==
                requestedRun.PaycheckPostedDate &&
            run.PaycheckAmount ==
                requestedRun.PaycheckAmount &&
            string.Equals(
                run.CurrencyCode,
                requestedRun.CurrencyCode,
                StringComparison.Ordinal) &&
            run.RecommendedSetAside ==
                requestedRun.RecommendedSetAside &&
            run.PaycheckRemainingAfterPlan ==
                requestedRun.PaycheckRemainingAfterPlan &&
            run.Shortfall ==
                requestedRun.Shortfall &&
            Matches(
                existing.Allocations,
                requestedRun.PaycheckPostedDate,
                requestedAllocations);
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

    private static void ValidateMoney(
        decimal amount,
        string parameterName)
    {
        if (amount <
            0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Money values cannot be negative.");
        }

        if (decimal.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero) !=
            amount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Money values must not contain fractions of a cent.");
        }
    }

    private static string NormalizeCurrency(
        string? currencyCode,
        string parameterName)
    {
        var currency =
            currencyCode?.Trim().ToUpperInvariant();

        if (currency is null ||
            currency.Length !=
                3 ||
            currency.Any(
                character =>
                    character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Currency code must be a three-letter alphabetic code.",
                parameterName);
        }

        return currency;
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

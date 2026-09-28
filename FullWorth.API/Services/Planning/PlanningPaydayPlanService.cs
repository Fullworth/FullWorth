using FullWorth.API.Services.Contracts;
using FullWorth.Core.Models.Planning;
using FullWorth.Core.Services;

namespace FullWorth.API.Services.Planning;

public enum PlanningPaydayPlanStatus
{
    Ready = 1,
    PayrollNotFound = 2,
    PayScheduleRequired = 3,
    PayrollFactUnsupported = 4
}

public enum PlanningPaydayPlanSkipReason
{
    MissingStatementFact = 1,
    MissingDueDate = 2,
    CurrencyMismatch = 3,
    InvalidStatementAmount = 4,
    PriorCurrencyConflict = 5
}

public sealed record PlanningPaydayPlanItem(
    Guid BillStreamId,
    string? ProviderName,
    Guid SourceStatementId,
    DateOnly BillPeriodEnd,
    DateOnly BillDueDate,
    decimal PlannedAmount,
    string CurrencyCode);

public sealed record PlanningPaydayPlanSkippedBill(
    Guid BillStreamId,
    string ProviderName,
    PlanningPaydayPlanSkipReason Reason);

public sealed record PlanningPaydayPlanSnapshot(
    PlanningPaydayPlanStatus Status,
    bool IsReplay,
    Guid PayrollTransactionId,
    DateOnly PaycheckPostedDate,
    decimal PaycheckAmount,
    string? CurrencyCode,
    decimal RecommendedSetAside,
    decimal PaycheckRemainingAfterPlan,
    decimal Shortfall,
    IReadOnlyList<PlanningPaydayPlanItem> Items,
    IReadOnlyList<PlanningPaydayPlanSkippedBill> SkippedBills);

public sealed class PlanningPaydayPlanService(
    PlanningSettingsService settingsService,
    PlanningPaycheckAllocationStore allocationStore,
    IBillStreamReadGateway billStreamGateway,
    IBillPlanningFactsGateway billPlanningFactsGateway,
    IPlanningPostedPayrollFactsGateway payrollFactsGateway)
{
    public async Task<PlanningPaydayPlanSnapshot> GenerateAsync(
        Guid userId,
        Guid payrollTransactionId,
        DateOnly postedDate,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (payrollTransactionId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Payroll transaction ID is required.",
                nameof(payrollTransactionId));
        }

        var payrollFacts =
            await payrollFactsGateway.GetAsync(
                userId,
                postedDate,
                postedDate,
                cancellationToken);

        var payroll =
            payrollFacts
                .SingleOrDefault(
                    candidate =>
                        candidate.TransactionId ==
                        payrollTransactionId);

        if (payroll is null)
        {
            return Empty(
                PlanningPaydayPlanStatus.PayrollNotFound,
                payrollTransactionId,
                postedDate);
        }

        if (!TryNormalizeCurrency(
                payroll.CurrencyCode,
                out var payrollCurrency) ||
            !IsMoney(
                payroll.Amount) ||
            payroll.Amount <=
                0m)
        {
            return Empty(
                PlanningPaydayPlanStatus.PayrollFactUnsupported,
                payrollTransactionId,
                postedDate,
                payroll.Amount,
                payrollCurrency);
        }

        var existing =
            await allocationStore.GetForPaycheckAsync(
                userId,
                payrollTransactionId,
                cancellationToken);

        var activeBills =
            await billStreamGateway.ListOwnedActiveAsync(
                userId,
                cancellationToken);

        var activeBillMap =
            activeBills.ToDictionary(
                bill =>
                    bill.BillStreamId);

        if (existing.Count >
            0)
        {
            var replayItems =
                existing
                    .Select(
                        allocation =>
                            new PlanningPaydayPlanItem(
                                allocation.BillStreamId,
                                activeBillMap.TryGetValue(
                                    allocation.BillStreamId,
                                    out var bill)
                                    ? bill.ProviderName
                                    : null,
                                allocation.SourceStatementId,
                                allocation.BillPeriodEnd,
                                allocation.BillDueDate,
                                allocation.PlannedAmount,
                                allocation.CurrencyCode))
                    .OrderBy(
                        item =>
                            item.BillDueDate)
                    .ThenBy(
                        item =>
                            item.ProviderName,
                        StringComparer.OrdinalIgnoreCase)
                    .ThenBy(
                        item =>
                            item.BillStreamId)
                    .ToList();

            return Ready(
                isReplay: true,
                payrollTransactionId,
                postedDate,
                payroll.Amount,
                payrollCurrency!,
                replayItems,
                skippedBills: []);
        }

        var schedule =
            await settingsService.GetPayScheduleAsync(
                userId,
                cancellationToken);

        if (schedule is null)
        {
            return Empty(
                PlanningPaydayPlanStatus.PayScheduleRequired,
                payrollTransactionId,
                postedDate,
                payroll.Amount,
                payrollCurrency);
        }

        if (activeBills.Count ==
            0)
        {
            return Ready(
                isReplay: false,
                payrollTransactionId,
                postedDate,
                payroll.Amount,
                payrollCurrency!,
                items: [],
                skippedBills: []);
        }

        var billIds =
            activeBills
                .Select(
                    bill =>
                        bill.BillStreamId)
                .ToArray();

        var facts =
            await billPlanningFactsGateway.GetLatestAsync(
                userId,
                billIds,
                cancellationToken);

        var preferences =
            await settingsService.GetBillFundingPreferencesAsync(
                userId,
                cancellationToken);

        var preferenceMap =
            preferences.ToDictionary(
                preference =>
                    preference.BillStreamId,
                preference =>
                    preference.PaychecksAheadOverride);

        var priorTotals =
            await allocationStore.GetPriorTotalsAsync(
                userId,
                billIds,
                postedDate,
                payrollTransactionId,
                cancellationToken);

        var priorByCycle =
            priorTotals
                .GroupBy(
                    total =>
                        new
                        {
                            total.BillStreamId,
                            total.BillPeriodEnd
                        })
                .ToDictionary(
                    group =>
                        group.Key,
                    group =>
                        group.ToList());

        var generatorInputs =
            new List<PaydayPlanBillInput>();

        var usableFacts =
            new Dictionary<Guid, BillPlanningFact>();

        var skipped =
            new List<PlanningPaydayPlanSkippedBill>();

        foreach (var bill in
                 activeBills)
        {
            if (!facts.TryGetValue(
                    bill.BillStreamId,
                    out var fact))
            {
                skipped.Add(
                    new PlanningPaydayPlanSkippedBill(
                        bill.BillStreamId,
                        bill.ProviderName,
                        PlanningPaydayPlanSkipReason.MissingStatementFact));

                continue;
            }

            if (fact.DueDate is null)
            {
                skipped.Add(
                    new PlanningPaydayPlanSkippedBill(
                        bill.BillStreamId,
                        bill.ProviderName,
                        PlanningPaydayPlanSkipReason.MissingDueDate));

                continue;
            }

            if (!IsMoney(
                    fact.Amount))
            {
                skipped.Add(
                    new PlanningPaydayPlanSkippedBill(
                        bill.BillStreamId,
                        bill.ProviderName,
                        PlanningPaydayPlanSkipReason.InvalidStatementAmount));

                continue;
            }

            if (!TryNormalizeCurrency(
                    fact.CurrencyCode,
                    out var billCurrency) ||
                !string.Equals(
                    billCurrency,
                    payrollCurrency,
                    StringComparison.Ordinal))
            {
                skipped.Add(
                    new PlanningPaydayPlanSkippedBill(
                        bill.BillStreamId,
                        bill.ProviderName,
                        PlanningPaydayPlanSkipReason.CurrencyMismatch));

                continue;
            }

            priorByCycle.TryGetValue(
                new
                {
                    fact.BillStreamId,
                    BillPeriodEnd =
                        fact.PeriodEnd
                },
                out var cycleTotals);

            cycleTotals ??=
                [];

            if (cycleTotals.Any(
                    total =>
                        !string.Equals(
                            NormalizeStoredCurrency(
                                total.CurrencyCode),
                            billCurrency,
                            StringComparison.Ordinal)))
            {
                skipped.Add(
                    new PlanningPaydayPlanSkippedBill(
                        bill.BillStreamId,
                        bill.ProviderName,
                        PlanningPaydayPlanSkipReason.PriorCurrencyConflict));

                continue;
            }

            var alreadyPlanned =
                cycleTotals.Sum(
                    total =>
                        total.PlannedAmount);

            var paychecksAhead =
                preferenceMap.TryGetValue(
                    bill.BillStreamId,
                    out var configuredOverride)
                    ? configuredOverride
                    : schedule.DefaultPaychecksAhead;

            generatorInputs.Add(
                new PaydayPlanBillInput(
                    bill.BillStreamId,
                    bill.ProviderName,
                    fact.Amount,
                    alreadyPlanned,
                    fact.DueDate.Value,
                    paychecksAhead,
                    billCurrency!));

            usableFacts.Add(
                bill.BillStreamId,
                fact);
        }

        var plan =
            PaydayPlanGenerator.Generate(
                new PaydayPlanRequest(
                    payroll.Amount,
                    postedDate,
                    payrollCurrency!,
                    new PayScheduleDefinition(
                        schedule.Frequency,
                        schedule.AnchorPayDate,
                        schedule.SecondaryDayOfMonth),
                    generatorInputs));

        var drafts =
            plan.Bills
                .Where(
                    bill =>
                        bill.RecommendedSetAsideFromCurrentPaycheck >
                        0m)
                .Select(
                    bill =>
                    {
                        var fact =
                            usableFacts[bill.BillStreamId];

                        return new PlanningPaycheckAllocationDraft(
                            bill.BillStreamId,
                            fact.StatementId,
                            fact.PeriodEnd,
                            fact.DueDate!.Value,
                            bill.RecommendedSetAsideFromCurrentPaycheck,
                            bill.CurrencyCode);
                    })
                .ToArray();

        var saved =
            await allocationStore.SavePaycheckPlanAsync(
                userId,
                payrollTransactionId,
                postedDate,
                drafts,
                cancellationToken);

        var savedByCycle =
            saved.ToDictionary(
                allocation =>
                    new
                    {
                        allocation.BillStreamId,
                        allocation.BillPeriodEnd
                    });

        var items =
            plan.Bills
                .Where(
                    bill =>
                        bill.RecommendedSetAsideFromCurrentPaycheck >
                        0m)
                .Select(
                    bill =>
                    {
                        var fact =
                            usableFacts[bill.BillStreamId];

                        var savedAllocation =
                            savedByCycle[
                                new
                                {
                                    bill.BillStreamId,
                                    fact.PeriodEnd
                                }];

                        return new PlanningPaydayPlanItem(
                            bill.BillStreamId,
                            bill.ProviderName,
                            savedAllocation.SourceStatementId,
                            savedAllocation.BillPeriodEnd,
                            savedAllocation.BillDueDate,
                            savedAllocation.PlannedAmount,
                            savedAllocation.CurrencyCode);
                    })
                .OrderBy(
                    item =>
                        item.BillDueDate)
                .ThenBy(
                    item =>
                        item.ProviderName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    item =>
                        item.BillStreamId)
                .ToList();

        return Ready(
            isReplay: false,
            payrollTransactionId,
            postedDate,
            payroll.Amount,
            payrollCurrency!,
            items,
            skipped);
    }

    private static PlanningPaydayPlanSnapshot Ready(
        bool isReplay,
        Guid payrollTransactionId,
        DateOnly postedDate,
        decimal paycheckAmount,
        string currencyCode,
        IReadOnlyList<PlanningPaydayPlanItem> items,
        IReadOnlyList<PlanningPaydayPlanSkippedBill> skippedBills)
    {
        var recommended =
            RoundMoney(
                items.Sum(
                    item =>
                        item.PlannedAmount));

        return new PlanningPaydayPlanSnapshot(
            PlanningPaydayPlanStatus.Ready,
            isReplay,
            payrollTransactionId,
            postedDate,
            RoundMoney(
                paycheckAmount),
            currencyCode,
            recommended,
            RoundMoney(
                Math.Max(
                    0m,
                    paycheckAmount -
                    recommended)),
            RoundMoney(
                Math.Max(
                    0m,
                    recommended -
                    paycheckAmount)),
            items,
            skippedBills);
    }

    private static PlanningPaydayPlanSnapshot Empty(
        PlanningPaydayPlanStatus status,
        Guid payrollTransactionId,
        DateOnly postedDate,
        decimal paycheckAmount = 0m,
        string? currencyCode = null) =>
        new(
            status,
            IsReplay: false,
            payrollTransactionId,
            postedDate,
            IsMoney(
                paycheckAmount)
                ? RoundMoney(
                    paycheckAmount)
                : 0m,
            currencyCode,
            RecommendedSetAside: 0m,
            PaycheckRemainingAfterPlan:
                IsMoney(
                    paycheckAmount)
                    ? RoundMoney(
                        Math.Max(
                            0m,
                            paycheckAmount))
                    : 0m,
            Shortfall: 0m,
            Items: [],
            SkippedBills: []);

    private static bool TryNormalizeCurrency(
        string? currencyCode,
        out string? normalized)
    {
        normalized =
            currencyCode?.Trim().ToUpperInvariant();

        if (normalized is null ||
            normalized.Length !=
                3 ||
            normalized.Any(
                character =>
                    character is < 'A' or > 'Z'))
        {
            normalized =
                null;

            return false;
        }

        return true;
    }

    private static string NormalizeStoredCurrency(
        string currencyCode) =>
        currencyCode
            .Trim()
            .ToUpperInvariant();

    private static bool IsMoney(
        decimal amount) =>
        amount >=
            0m &&
        decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero) ==
        amount;

    private static decimal RoundMoney(
        decimal amount) =>
        decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero);
}

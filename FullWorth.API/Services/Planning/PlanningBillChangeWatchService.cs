using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Planning;

public enum PlanningBillChangeWatchRecalculationStatus
{
    Ready = 1,
    UnsupportedCurrentBillFact = 2,
    PriorCurrencyConflict = 3,
    EvidenceMismatch = 4
}

public sealed record PlanningBillChangeWatchItem(
    Guid BillStreamId,
    string ProviderName,
    Guid ChangeId,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    DateOnly BillPeriodEnd,
    DateOnly? BillDueDate,
    string? CurrencyCode,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    string Description,
    PlanningBillChangeWatchRecalculationStatus RecalculationStatus,
    decimal? AlreadyPlanned,
    decimal? RemainingAmountToPlan);

public sealed class PlanningBillChangeWatchService(
    IBillStreamReadGateway billStreamGateway,
    IBillPlanningFactsGateway billPlanningFactsGateway,
    IBillPlanningChangeFactsGateway billChangeFactsGateway,
    PlanningPaycheckAllocationStore allocationStore)
{
    public async Task<IReadOnlyList<PlanningBillChangeWatchItem>> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        var activeBills =
            await billStreamGateway.ListOwnedActiveAsync(
                userId,
                cancellationToken);

        if (activeBills.Count ==
            0)
        {
            return [];
        }

        var billIds =
            activeBills
                .Select(
                    bill =>
                        bill.BillStreamId)
                .ToArray();

        var billFactsTask =
            billPlanningFactsGateway.GetLatestAsync(
                userId,
                billIds,
                cancellationToken);

        var changeFactsTask =
            billChangeFactsGateway.GetLatestConfirmedAsync(
                userId,
                billIds,
                cancellationToken);

        var plannedTotalsTask =
            allocationStore.GetCycleTotalsAsync(
                userId,
                billIds,
                cancellationToken);

        await Task.WhenAll(
            billFactsTask,
            changeFactsTask,
            plannedTotalsTask);

        var billFacts =
            await billFactsTask;

        var changeFacts =
            await changeFactsTask;

        var plannedTotals =
            await plannedTotalsTask;

        var totalsByCycle =
            plannedTotals
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

        var results =
            new List<PlanningBillChangeWatchItem>();

        foreach (var bill in
                 activeBills)
        {
            if (!billFacts.TryGetValue(
                    bill.BillStreamId,
                    out var currentFact) ||
                !changeFacts.TryGetValue(
                    bill.BillStreamId,
                    out var changeFact))
            {
                continue;
            }

            /*
             * Only surface change evidence for the exact latest statement that
             * Planning is currently using. An older confirmed change remains
             * valid history, but it is not an upcoming-change signal anymore.
             */
            if (changeFact.CurrentStatementId !=
                    currentFact.StatementId ||
                changeFact.CurrentPeriodEnd !=
                    currentFact.PeriodEnd)
            {
                continue;
            }

            var status =
                PlanningBillChangeWatchRecalculationStatus.Ready;

            decimal? alreadyPlanned =
                null;

            decimal? remainingAmount =
                null;

            string? currencyCode =
                null;

            if (!TryNormalizeCurrency(
                    currentFact.CurrencyCode,
                    out currencyCode) ||
                !IsMoney(
                    currentFact.Amount))
            {
                status =
                    PlanningBillChangeWatchRecalculationStatus
                        .UnsupportedCurrentBillFact;
            }
            else if (!IsMoney(
                         changeFact.CurrentAmount) ||
                     changeFact.CurrentAmount !=
                         currentFact.Amount)
            {
                status =
                    PlanningBillChangeWatchRecalculationStatus
                        .EvidenceMismatch;
            }
            else
            {
                totalsByCycle.TryGetValue(
                    new
                    {
                        currentFact.BillStreamId,
                        BillPeriodEnd =
                            currentFact.PeriodEnd
                    },
                    out var cycleTotals);

                cycleTotals ??=
                    [];

                if (cycleTotals.Any(
                        total =>
                            !TryNormalizeCurrency(
                                total.CurrencyCode,
                                out var totalCurrency) ||
                            !string.Equals(
                                totalCurrency,
                                currencyCode,
                                StringComparison.Ordinal)))
                {
                    status =
                        PlanningBillChangeWatchRecalculationStatus
                            .PriorCurrencyConflict;
                }
                else
                {
                    alreadyPlanned =
                        RoundMoney(
                            cycleTotals.Sum(
                                total =>
                                    total.PlannedAmount));

                    remainingAmount =
                        RoundMoney(
                            Math.Max(
                                0m,
                                currentFact.Amount -
                                alreadyPlanned.Value));
                }
            }

            results.Add(
                new PlanningBillChangeWatchItem(
                    bill.BillStreamId,
                    bill.ProviderName,
                    changeFact.ChangeId,
                    changeFact.PreviousStatementId,
                    changeFact.CurrentStatementId,
                    currentFact.PeriodEnd,
                    currentFact.DueDate,
                    currencyCode,
                    changeFact.PreviousAmount,
                    changeFact.CurrentAmount,
                    changeFact.AmountDifference,
                    changeFact.AnnualizedImpact,
                    changeFact.Description,
                    status,
                    alreadyPlanned,
                    remainingAmount));
        }

        return results
            .OrderBy(
                item =>
                    item.BillDueDate.HasValue
                        ? 0
                        : 1)
            .ThenBy(
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
    }

    private static bool TryNormalizeCurrency(
        string? currencyCode,
        out string? normalized)
    {
        normalized =
            currencyCode?
                .Trim()
                .ToUpperInvariant();

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

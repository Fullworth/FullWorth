namespace FullWorth.API.Services.Planning;

/// <summary>
/// Immutable coverage of the owned active bill set evaluated at generation.
/// Eligible includes supported bills requiring no new allocation. This does not
/// prove that every real-world obligation was imported or discovered.
/// </summary>
public sealed record PlanningPaycheckPlanCoverage(
    int EligibleBillCount,
    int MissingStatementFactCount,
    int MissingDueDateCount,
    int CurrencyMismatchCount,
    int InvalidStatementAmountCount,
    int PriorCurrencyConflictCount)
{
    public int SkippedBillCount => MissingStatementFactCount + MissingDueDateCount +
        CurrencyMismatchCount + InvalidStatementAmountCount + PriorCurrencyConflictCount;

    public int EvaluatedBillCount => EligibleBillCount + SkippedBillCount;

    public bool HasExcludedBills => SkippedBillCount > 0;

    internal void Validate()
    {
        if (!IsValid())
        {
            throw new ArgumentOutOfRangeException(nameof(PlanningPaycheckPlanCoverage),
                "Coverage must contain nonnegative counts for at most 500 evaluated bills.");
        }
    }

    private bool IsValid() =>
        new[] { EligibleBillCount, MissingStatementFactCount, MissingDueDateCount, CurrencyMismatchCount, InvalidStatementAmountCount, PriorCurrencyConflictCount }
            .All(count => count is >= 0 and <= 500) &&
        (long)EligibleBillCount + MissingStatementFactCount + MissingDueDateCount +
            CurrencyMismatchCount + InvalidStatementAmountCount + PriorCurrencyConflictCount <= 500;

    // Null means historical/unsupported coverage, never zero excluded bills.
    public static PlanningPaycheckPlanCoverage? FromStored(
        int? eligibleBillCount,
        int? missingStatementFactCount,
        int? missingDueDateCount,
        int? currencyMismatchCount,
        int? invalidStatementAmountCount,
        int? priorCurrencyConflictCount)
    {
        if (eligibleBillCount is null ||
            missingStatementFactCount is null ||
            missingDueDateCount is null ||
            currencyMismatchCount is null ||
            invalidStatementAmountCount is null ||
            priorCurrencyConflictCount is null)
        {
            return null;
        }

        var coverage = new PlanningPaycheckPlanCoverage(
            eligibleBillCount.Value,
            missingStatementFactCount.Value,
            missingDueDateCount.Value,
            currencyMismatchCount.Value,
            invalidStatementAmountCount.Value,
            priorCurrencyConflictCount.Value);
        return coverage.IsValid() ? coverage : null;
    }
}

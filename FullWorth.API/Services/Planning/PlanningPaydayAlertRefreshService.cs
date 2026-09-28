using System.Globalization;
using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Planning;

public sealed class PlanningPaydayAlertRefreshService(
    PlanningPaydayPlanService paydayPlanService,
    IPlanningPostedPayrollFactsGateway payrollFactsGateway,
    IPlanningPaydayAlertGateway alertGateway,
    TimeProvider timeProvider)
    : IPlanningPaydayAlertRefreshGateway
{
    private const int PayrollLookbackDays =
        7;

    public async Task<PlanningPaydayAlertRefreshResult> RefreshAsync(
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

        var today =
            DateOnly.FromDateTime(
                timeProvider.GetUtcNow()
                    .UtcDateTime);

        var payrollFacts =
            await payrollFactsGateway.GetAsync(
                userId,
                today.AddDays(
                    -PayrollLookbackDays),
                today,
                cancellationToken);

        var scanned =
            0;

        var ready =
            0;

        var skipped =
            0;

        foreach (var payroll in
                 payrollFacts
                     .GroupBy(
                         fact =>
                             fact.TransactionId)
                     .Select(
                         group =>
                             group
                                 .OrderByDescending(
                                     fact =>
                                         fact.PostedDate)
                                 .First())
                     .OrderBy(
                         fact =>
                             fact.PostedDate)
                     .ThenBy(
                         fact =>
                             fact.TransactionId))
        {
            cancellationToken.ThrowIfCancellationRequested();

            scanned++;

            var plan =
                await paydayPlanService.GenerateAsync(
                    userId,
                    payroll.TransactionId,
                    payroll.PostedDate,
                    cancellationToken);

            switch (plan.Status)
            {
                case PlanningPaydayPlanStatus.Ready:
                    await alertGateway.EnsureAsync(
                        userId,
                        CreateAlert(
                            plan),
                        timeProvider.GetUtcNow(),
                        cancellationToken);

                    ready++;

                    break;

                case PlanningPaydayPlanStatus.PayScheduleRequired:
                    /*
                     * Pay schedule is user-level configuration. Once one
                     * candidate reports it missing, every remaining candidate
                     * would report the same state and create no useful work.
                     */
                    return new PlanningPaydayAlertRefreshResult(
                        scanned,
                        ready,
                        skipped,
                        PayScheduleRequired:
                            true);

                case PlanningPaydayPlanStatus.PayrollNotFound:
                case PlanningPaydayPlanStatus.PayrollFactUnsupported:
                    skipped++;

                    break;

                default:
                    throw new InvalidOperationException(
                        "Planning payday plan status is invalid.");
            }
        }

        return new PlanningPaydayAlertRefreshResult(
            scanned,
            ready,
            skipped,
            PayScheduleRequired:
                false);
    }

    private static PlanningPaydayAlertRequest CreateAlert(
        PlanningPaydayPlanSnapshot plan)
    {
        if (plan.Status !=
                PlanningPaydayPlanStatus.Ready ||
            string.IsNullOrWhiteSpace(
                plan.CurrencyCode))
        {
            throw new InvalidOperationException(
                "Only complete payday plans can create alerts.");
        }

        var currency =
            plan.CurrencyCode
                .Trim()
                .ToUpperInvariant();

        if (plan.Shortfall >
            0m)
        {
            return new PlanningPaydayAlertRequest(
                plan.PayrollTransactionId,
                PlanningPaydayAlertSeverity.Warning,
                "Payday plan needs review",
                $"Upcoming bill recommendations total {Money(plan.RecommendedSetAside)} {currency}, " +
                $"{Money(plan.Shortfall)} {currency} more than this {Money(plan.PaycheckAmount)} {currency} paycheck. " +
                "Review the plan before relying on it.");
        }

        if (plan.RecommendedSetAside >
            0m)
        {
            return new PlanningPaydayAlertRequest(
                plan.PayrollTransactionId,
                PlanningPaydayAlertSeverity.Info,
                "Payday plan ready",
                $"FullWorth recommends planning {Money(plan.RecommendedSetAside)} {currency} " +
                $"of this {Money(plan.PaycheckAmount)} {currency} paycheck for upcoming bills, " +
                $"leaving {Money(plan.PaycheckRemainingAfterPlan)} {currency} unplanned.");
        }

        return new PlanningPaydayAlertRequest(
            plan.PayrollTransactionId,
            PlanningPaydayAlertSeverity.Info,
            "Payday plan ready",
            $"No bill allocation is recommended from this {Money(plan.PaycheckAmount)} {currency} paycheck " +
            "based on the bill evidence currently available.");
    }

    private static string Money(
        decimal amount) =>
        amount.ToString(
            "0.00",
            CultureInfo.InvariantCulture);
}

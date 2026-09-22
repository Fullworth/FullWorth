using FullWorth.Core.Models;

namespace FullWorth.Core.Services;

public sealed class RecurringBillDetectionService
{
    private const int MinimumMonthlyDays = 23;
    private const int MaximumMonthlyDays = 38;
    private const decimal ApproximateMonthDays = 30.4375m;
    private readonly BillMerchantNormalizer _merchantNormalizer = new();

    public IReadOnlyList<RecurringBillDetectionResult> Detect(
        IEnumerable<BankTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var postedTransactions = transactions
            .Where(transaction => !transaction.IsPending)
            .OrderBy(transaction => transaction.PostedDate)
            .ToList();

        var results = new List<RecurringBillDetectionResult>();

        foreach (var merchantGroup in postedTransactions.GroupBy(
                     transaction => _merchantNormalizer.Normalize(
                         transaction.MerchantName),
                     StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(merchantGroup.Key))
            {
                continue;
            }

            var merchantTransactions = merchantGroup
                .OrderBy(transaction => transaction.PostedDate)
                .ToList();

            if (merchantTransactions.Count < 2)
            {
                continue;
            }

            var intervals = new List<int>();

            for (var i = 1; i < merchantTransactions.Count; i++)
            {
                var daysBetween =
                    merchantTransactions[i].PostedDate.DayNumber -
                    merchantTransactions[i - 1].PostedDate.DayNumber;

                if (daysBetween > 0)
                {
                    intervals.Add(daysBetween);
                }
            }

            if (!IsMonthlyCadence(intervals))
            {
                continue;
            }

            var latestTransaction =
                merchantTransactions[^1];

            var displayMerchantName =
                merchantTransactions
                    .GroupBy(
                        transaction => transaction.MerchantName,
                        StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Key;

            var previousTransactions =
                merchantTransactions
                    .Take(merchantTransactions.Count - 1)
                    .ToList();

            var historicalAverage = decimal.Round(
                previousTransactions.Average(
                    transaction => transaction.Amount),
                2,
                MidpointRounding.AwayFromZero);

            var latestDifference = decimal.Round(
                latestTransaction.Amount - historicalAverage,
                2,
                MidpointRounding.AwayFromZero);

            var latestPercentageChange =
                historicalAverage == 0m
                    ? 0m
                    : decimal.Round(
                        latestDifference /
                        historicalAverage *
                        100m,
                        2,
                        MidpointRounding.AwayFromZero);

            var minimumAmount =
                merchantTransactions.Min(
                    transaction => transaction.Amount);

            var maximumAmount =
                merchantTransactions.Max(
                    transaction => transaction.Amount);

            var hasVariableAmount =
                maximumAmount - minimumAmount > 1.00m;

            var hasMeaningfulChange =
                Math.Abs(latestDifference) >= 5.00m &&
                Math.Abs(latestPercentageChange) >= 10.00m;

            results.Add(
                new RecurringBillDetectionResult(
                    MerchantName: displayMerchantName,
                    Frequency: RecurringBillFrequency.Monthly,
                    AverageAmount: historicalAverage,
                    LatestAmount: latestTransaction.Amount,
                    LatestChargeDate: latestTransaction.PostedDate,
                    TransactionCount: merchantTransactions.Count,
                    HasVariableAmount: hasVariableAmount,
                    LatestDifference: latestDifference,
                    LatestPercentageChange: latestPercentageChange,
                    HasMeaningfulChange: hasMeaningfulChange));
        }

        return results
            .OrderByDescending(result => result.LatestAmount)
            .ToList()
            .AsReadOnly();
    }

    private static bool IsMonthlyCadence(
        IReadOnlyCollection<int> intervals)
    {
        if (intervals.Count == 0)
        {
            return false;
        }

        /*
         * Two observations are enough to establish a monthly cadence
         * candidate when the single interval is directly monthly. The API
         * persistence layer still requires stronger category/subscription
         * evidence before a two-observation candidate becomes a Bill Stream.
         */
        if (intervals.Count == 1)
        {
            return IsDirectMonthlyInterval(
                intervals.First());
        }

        var directMonthlyIntervals =
            intervals.Count(IsDirectMonthlyInterval);

        var monthlyEquivalentIntervals =
            intervals.Count(IsMonthlyEquivalentInterval);

        var minimumEquivalentIntervals =
            (int)Math.Ceiling(
                intervals.Count * 0.75m);

        var minimumDirectMonthlyIntervals =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    intervals.Count * 0.50m));

        return directMonthlyIntervals >=
                   minimumDirectMonthlyIntervals &&
               monthlyEquivalentIntervals >=
                   minimumEquivalentIntervals;
    }

    private static bool IsDirectMonthlyInterval(
        int days)
    {
        return days >= MinimumMonthlyDays &&
               days <= MaximumMonthlyDays;
    }

    private static bool IsMonthlyEquivalentInterval(
        int days)
    {
        if (IsDirectMonthlyInterval(days))
        {
            return true;
        }

        /*
         * Allow one skipped/missing monthly observation without turning
         * genuinely quarterly charges into monthly Bill Streams.
         *
         * Example: 30 days followed by 60 days still has evidence of a
         * monthly cadence. Repeated 90-day intervals do not.
         */
        var inferredCycles =
            (int)Math.Round(
                days / ApproximateMonthDays,
                MidpointRounding.AwayFromZero);

        if (inferredCycles != 2)
        {
            return false;
        }

        var daysPerCycle =
            days / (decimal)inferredCycles;

        return daysPerCycle >= MinimumMonthlyDays &&
               daysPerCycle <= MaximumMonthlyDays;
    }
}

public enum RecurringBillFrequency
{
    Monthly
}

public sealed record RecurringBillDetectionResult(
    string MerchantName,
    RecurringBillFrequency Frequency,
    decimal AverageAmount,
    decimal LatestAmount,
    DateOnly LatestChargeDate,
    int TransactionCount,
    bool HasVariableAmount,
    decimal LatestDifference,
    decimal LatestPercentageChange,
    bool HasMeaningfulChange);

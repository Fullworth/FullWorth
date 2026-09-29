using FullWorth.Core.Models.Planning;

namespace FullWorth.Core.Services;

public sealed record PaydayPlanBillInput(
    Guid BillStreamId,
    string ProviderName,
    decimal AmountDue,
    decimal AlreadyPlanned,
    DateOnly DueDate,
    int PaychecksAhead,
    string CurrencyCode);

public sealed record PaydayPlanRequest(
    decimal PaycheckAmount,
    DateOnly CurrentPayDate,
    string CurrencyCode,
    PayScheduleDefinition Schedule,
    IReadOnlyList<PaydayPlanBillInput> Bills);

public sealed record PaydayPlanBillResult(
    Guid BillStreamId,
    string ProviderName,
    string CurrencyCode,
    DateOnly DueDate,
    BillFundingWindowStatus Status,
    decimal AmountDue,
    decimal AlreadyPlanned,
    decimal RemainingAmount,
    int PaychecksRemaining,
    decimal RecommendedSetAsideFromCurrentPaycheck,
    IReadOnlyList<DateOnly> RemainingPayDates);

public sealed record PaydayPlanResult(
    decimal PaycheckAmount,
    string CurrencyCode,
    DateOnly CurrentPayDate,
    decimal RecommendedSetAside,
    decimal PaycheckRemainingAfterPlan,
    decimal Shortfall,
    IReadOnlyList<PaydayPlanBillResult> Bills);

public static class PaydayPlanGenerator
{
    public static PaydayPlanResult Generate(
        PaydayPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        ArgumentNullException.ThrowIfNull(
            request.Schedule);

        ArgumentNullException.ThrowIfNull(
            request.Bills);

        ValidateMoney(
            request.PaycheckAmount,
            nameof(request.PaycheckAmount));

        var paycheckCurrency =
            NormalizeCurrency(
                request.CurrencyCode,
                nameof(request.CurrencyCode));

        var billResults =
            new List<PaydayPlanBillResult>(
                request.Bills.Count);

        decimal recommendedSetAside =
            0m;

        foreach (var bill in
                 request.Bills)
        {
            ArgumentNullException.ThrowIfNull(
                bill);

            if (bill.BillStreamId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "Bill stream ID is required.",
                    nameof(request));
            }

            if (string.IsNullOrWhiteSpace(
                    bill.ProviderName))
            {
                throw new ArgumentException(
                    "Bill provider name is required.",
                    nameof(request));
            }

            var billCurrency =
                NormalizeCurrency(
                    bill.CurrencyCode,
                    nameof(request));

            if (!string.Equals(
                    billCurrency,
                    paycheckCurrency,
                    StringComparison.Ordinal))
            {
                /*
                 * FullWorth must not invent an FX conversion. A future
                 * multi-currency plan needs an explicit trusted exchange-rate
                 * source and deterministic conversion policy.
                 */
                throw new InvalidOperationException(
                    "Payday planning cannot combine different currencies without an explicit exchange-rate source.");
            }

            var calculation =
                PaycheckBillPlanCalculator.Calculate(
                    new PaycheckBillPlanRequest(
                        bill.AmountDue,
                        bill.AlreadyPlanned,
                        bill.DueDate,
                        request.CurrentPayDate,
                        bill.PaychecksAhead,
                        request.Schedule));

            recommendedSetAside =
                checked(
                    recommendedSetAside +
                    calculation
                        .RecommendedSetAsideFromCurrentPaycheck);

            billResults.Add(
                new PaydayPlanBillResult(
                    bill.BillStreamId,
                    bill.ProviderName.Trim(),
                    billCurrency,
                    bill.DueDate,
                    calculation.Status,
                    calculation.AmountDue,
                    calculation.AlreadySetAside,
                    calculation.RemainingAmount,
                    calculation.PaychecksRemaining,
                    calculation
                        .RecommendedSetAsideFromCurrentPaycheck,
                    calculation.RemainingPayDates));
        }

        recommendedSetAside =
            RoundMoney(
                recommendedSetAside);

        var shortfall =
            Math.Max(
                0m,
                recommendedSetAside -
                request.PaycheckAmount);

        var paycheckRemaining =
            Math.Max(
                0m,
                request.PaycheckAmount -
                recommendedSetAside);

        var orderedBills =
            billResults
                .OrderBy(
                    bill =>
                        bill.DueDate)
                .ThenBy(
                    bill =>
                        bill.ProviderName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    bill =>
                        bill.BillStreamId)
                .ToList();

        return new PaydayPlanResult(
            RoundMoney(
                request.PaycheckAmount),
            paycheckCurrency,
            request.CurrentPayDate,
            recommendedSetAside,
            RoundMoney(
                paycheckRemaining),
            RoundMoney(
                shortfall),
            orderedBills);
    }

    private static string NormalizeCurrency(
        string? value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            throw new ArgumentException(
                "Currency code is required.",
                parameterName);
        }

        var normalized =
            value.Trim()
                .ToUpperInvariant();

        if (normalized.Length !=
                3 ||
            normalized.Any(
                character =>
                    character is < 'A' or > 'Z'))
        {
            throw new ArgumentException(
                "Currency code must be a three-letter alphabetic code.",
                parameterName);
        }

        return normalized;
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

    private static decimal RoundMoney(
        decimal amount) =>
        decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero);
}

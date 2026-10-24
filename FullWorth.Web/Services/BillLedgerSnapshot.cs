using System.Globalization;
using FullWorth.Core.Formatting;

namespace FullWorth.Web.Services;

public enum BillLedgerFilter { All, Increased, Decreased, NoHistory, Unchanged, Unavailable }
public enum BillLedgerSort { Attention, Name, LatestCharge, Increase }

public sealed record BillLedgerEntry(Guid Id, string ProviderName, string Category, bool IsActive,
    string Source, bool HasTransactionEvidence, bool HasMonthlyCadenceEvidence,
    decimal CurrentAmount, decimal PreviousAverage, string? CurrencyCode);

/// <summary>Private, read-only presentation of returned bills; never discovers or reactivates records.</summary>
public sealed class BillLedgerSnapshot
{
    public IReadOnlyList<BillLedgerEntry> Active { get; private init; } = [];
    public IReadOnlyList<BillLedgerEntry> Review { get; private init; } = [];
    public IReadOnlyList<BillLedgerEntry> Inactive { get; private init; } = [];
    public int TotalCount => Active.Count + Review.Count + Inactive.Count;
    public int IncreasedCount { get; private init; }
    public int DecreasedCount { get; private init; }
    public int NoHistoryCount { get; private init; }
    public int UnchangedCount { get; private init; }
    public int UnavailableCount { get; private init; }
    public int KnownAmountCount => Active.Count - UnavailableCount;
    public string? RecognizedTotal { get; private init; }
    public string? IncreaseTotal { get; private init; }
    public string? AnnualizedIncreaseTotal { get; private init; }

    public static bool HasAmount(BillLedgerEntry bill) =>
        bill.HasTransactionEvidence && bill.CurrentAmount > 0m &&
        FinancialAmountFormatter.TryNormalizeCurrency(bill.CurrencyCode, out _);
    public static bool CanCompare(BillLedgerEntry bill) => HasAmount(bill) && bill.PreviousAverage > 0m;
    public static decimal Difference(BillLedgerEntry bill) => CanCompare(bill)
        ? decimal.Round(bill.CurrentAmount - bill.PreviousAverage, 2, MidpointRounding.AwayFromZero) : 0m;

    public static BillLedgerSnapshot Create(IEnumerable<BillLedgerEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var all = source.ToArray();
        if (all.Any(bill => bill is null || bill.Id == Guid.Empty) ||
            all.Select(bill => bill.Id).Distinct().Count() != all.Length)
            throw new InvalidOperationException("Invalid bill snapshot.");
        var active = all.Where(bill => bill.IsActive).ToArray();
        var known = active.Where(HasAmount).ToArray();
        var comparable = active.Where(CanCompare).ToArray();
        var increases = comparable.Where(bill => Difference(bill) > 0m).ToArray();
        return new()
        {
            Active = active,
            Review = all.Where(bill => !bill.IsActive && bill.Source == "AutomaticDiscovery").ToArray(),
            Inactive = all.Where(bill => !bill.IsActive && bill.Source != "AutomaticDiscovery").ToArray(),
            IncreasedCount = increases.Length,
            DecreasedCount = comparable.Count(bill => Difference(bill) < 0m),
            NoHistoryCount = known.Count(bill => bill.PreviousAverage <= 0m),
            UnchangedCount = comparable.Count(bill => Difference(bill) == 0m),
            UnavailableCount = active.Length - known.Length,
            RecognizedTotal = FinancialAmountFormatter.FormatTotals(known.Select(bill =>
                (bill.CurrentAmount, bill.CurrencyCode))),
            IncreaseTotal = FinancialAmountFormatter.FormatTotals(comparable.Select(bill =>
                (Math.Max(Difference(bill), 0m), bill.CurrencyCode))),
            AnnualizedIncreaseTotal = increases.Length > 0 &&
                increases.All(bill => bill.HasMonthlyCadenceEvidence)
                    ? FinancialAmountFormatter.FormatTotals(increases.Select(bill =>
                        (Difference(bill), bill.CurrencyCode)), 12m) : null
        };
    }

    public IReadOnlyList<BillLedgerEntry> Select(string? query, BillLedgerFilter filter,
        BillLedgerSort sort, string? currency = null)
    {
        var rows = Active.Where(bill => Matches(bill, query))
            .Where(bill => string.IsNullOrEmpty(currency) ||
                CurrencyKey(bill) == currency)
            .Where(bill => filter switch
            {
                BillLedgerFilter.Increased => CanCompare(bill) && Difference(bill) > 0m,
                BillLedgerFilter.Decreased => CanCompare(bill) && Difference(bill) < 0m,
                BillLedgerFilter.NoHistory => HasAmount(bill) && bill.PreviousAverage <= 0m,
                BillLedgerFilter.Unchanged => CanCompare(bill) && Difference(bill) == 0m,
                BillLedgerFilter.Unavailable => !HasAmount(bill),
                _ => true
            });
        IOrderedEnumerable<BillLedgerEntry> ordered = sort switch
        {
            // Review buckets are evidence-status categories, not a ranking of
            // financial magnitudes across currencies. Currency groups stay
            // separate before any within-currency difference ordering.
            BillLedgerSort.Attention => rows.OrderBy(AttentionRank)
                .ThenBy(CurrencyKey, StringComparer.Ordinal)
                .ThenByDescending(Difference),
            BillLedgerSort.LatestCharge => rows.OrderBy(bill => !HasAmount(bill))
                .ThenBy(CurrencyKey, StringComparer.Ordinal).ThenByDescending(bill => bill.CurrentAmount),
            BillLedgerSort.Increase => rows.OrderBy(bill => !CanCompare(bill))
                .ThenBy(CurrencyKey, StringComparer.Ordinal).ThenByDescending(Difference),
            _ => rows.OrderBy(bill => bill.ProviderName, StringComparer.OrdinalIgnoreCase)
        };
        return ordered.ThenBy(bill => bill.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(bill => bill.Id).ToArray();
    }

    private static int AttentionRank(BillLedgerEntry bill)
    {
        if (!CanCompare(bill))
            return 1; // Unknown amount or missing comparison evidence needs review.

        var difference = Difference(bill);
        return difference > 0m ? 0 : difference < 0m ? 2 : 3;
    }

    public static string CurrencyKey(BillLedgerEntry bill) =>
        FinancialAmountFormatter.TryNormalizeCurrency(bill.CurrencyCode, out var currency)
            ? currency : "?";

    public static bool Matches(BillLedgerEntry bill, string? query)
    {
        var text = (query ?? string.Empty).Trim();
        if (text.Length > 120) text = text[..120];
        if (text.Length == 0) return true;
        var comparison = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return new[] { bill.ProviderName, bill.Category, bill.CurrencyCode }
            .Any(value => comparison.IndexOf(value ?? string.Empty, text, options) >= 0);
    }
}

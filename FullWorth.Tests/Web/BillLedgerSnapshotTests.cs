using FullWorth.Web.Services;

namespace FullWorth.Tests.Web;

public sealed class BillLedgerSnapshotTests
{
    private static BillLedgerEntry Bill(string name = "Utility", decimal current = 20m,
        decimal previous = 10m, string? currency = "USD", bool evidence = true,
        bool monthly = true, bool active = true, string source = "Manual") =>
        new(Guid.NewGuid(), name, "Utilities", active, source, evidence, monthly, current, previous, currency);

    [Fact]
    public void Totals_SeparateCurrenciesAndExcludeInactiveReviewAndUnavailableAmounts()
    {
        var snapshot = BillLedgerSnapshot.Create([
            Bill(current: 20, previous: 10, currency: " eur "),
            Bill(current: 10, previous: 8),
            Bill(current: 999, currency: null),
            Bill(current: 999, evidence: false),
            Bill(active: false), Bill(active: false, source: "AutomaticDiscovery")
        ]);
        Assert.Equal("EUR 20.00 · $10.00", snapshot.RecognizedTotal);
        Assert.Equal("EUR 10.00 · $2.00", snapshot.IncreaseTotal);
        Assert.Equal("EUR 120.00 · $24.00", snapshot.AnnualizedIncreaseTotal);
        Assert.Equal(4, snapshot.Active.Count);
        Assert.Single(snapshot.Inactive);
        Assert.Single(snapshot.Review);
        Assert.Equal(6, snapshot.TotalCount);
        Assert.Equal(2, snapshot.KnownAmountCount);
        Assert.Equal(2, snapshot.UnavailableCount);
    }

    [Theory]
    [InlineData(0, 20, "USD", true)]
    [InlineData(-10, 20, "USD", true)]
    [InlineData(10, 20, null, true)]
    [InlineData(10, 20, "US", true)]
    [InlineData(10, 20, "USD", false)]
    public void UnavailableEvidence_NeverCountsAsDecreaseOrUnchanged(
        int current, int previous, string? currency, bool evidence)
    {
        var snapshot = BillLedgerSnapshot.Create([Bill(current: current, previous: previous,
            currency: currency, evidence: evidence)]);
        Assert.Equal(1, snapshot.UnavailableCount);
        Assert.Equal(0, snapshot.DecreasedCount);
        Assert.Equal(0, snapshot.UnchangedCount);
        Assert.Equal(0, snapshot.NoHistoryCount);
        Assert.Null(snapshot.RecognizedTotal);
        Assert.Null(snapshot.IncreaseTotal);
        Assert.Single(snapshot.Select(null, BillLedgerFilter.Unavailable, BillLedgerSort.Name));
    }

    [Fact]
    public void Filters_AreDisjointAndDoNotChangeSummary()
    {
        var snapshot = BillLedgerSnapshot.Create([
            Bill("Increase", 20, 10), Bill("Decrease", 5, 10), Bill("Steady", 10, 10),
            Bill("New", 10, 0), Bill("Unavailable", 0, 10)
        ]);
        foreach (var filter in Enum.GetValues<BillLedgerFilter>().Where(value => value != BillLedgerFilter.All))
            Assert.Single(snapshot.Select("", filter, BillLedgerSort.Name));
        Assert.Equal("$45.00", snapshot.RecognizedTotal);
        Assert.Equal("$10.00", snapshot.IncreaseTotal); // Decreases never offset increases.
        Assert.Empty(snapshot.Select("missing", BillLedgerFilter.All, BillLedgerSort.LatestCharge));
        Assert.Equal("$45.00", snapshot.RecognizedTotal);
    }

    [Theory]
    [InlineData(" cafe ", true)]
    [InlineData("UTILITIES", true)]
    [InlineData("usd", true)]
    [InlineData("not here", false)]
    [InlineData("", true)]
    [InlineData(null, true)]
    public void Search_IsTrimmedCaseAndAccentInsensitive(string? query, bool expected) =>
        Assert.Equal(expected, BillLedgerSnapshot.Matches(Bill("Café service"), query));

    [Fact]
    public void Search_CapsInputWithoutChangingProviderText()
    {
        var name = new string('x', 120);
        var bill = Bill(name);
        Assert.True(BillLedgerSnapshot.Matches(bill, name + "ignored"));
        Assert.Equal(name, bill.ProviderName);
    }

    [Fact]
    public void Sort_GroupsCurrenciesNeverComparesTheirPurchasingPowerAndPutsUnknownLast()
    {
        var eur = Bill("Euro", 20, 5, "EUR");
        var usd = Bill("Dollar", 100, 95);
        var unknown = Bill("Unknown", 9999, 1, null);
        var snapshot = BillLedgerSnapshot.Create([unknown, usd, eur]);
        Assert.Equal(new[] { eur.Id, usd.Id, unknown.Id },
            snapshot.Select("", BillLedgerFilter.All, BillLedgerSort.LatestCharge).Select(item => item.Id));
        Assert.Equal(new[] { eur.Id, usd.Id, unknown.Id },
            snapshot.Select("", BillLedgerFilter.All, BillLedgerSort.Increase).Select(item => item.Id));
        Assert.Equal(usd.Id, Assert.Single(snapshot.Select("dollar", BillLedgerFilter.Increased,
            BillLedgerSort.LatestCharge, "USD")).Id);
        Assert.Equal(unknown.Id, Assert.Single(snapshot.Select("", BillLedgerFilter.All,
            BillLedgerSort.Name, "?")).Id);
    }

    [Fact]
    public void AttentionSort_ReviewsEvidenceBeforeCalmWithoutComparingCurrencyPurchasingPower()
    {
        var euroIncrease = Bill("Euro increase", 23m, 10m, "EUR");
        var dollarIncrease = Bill("Dollar increase", 100m, 92m, "USD");
        var absentAmount = Bill("Missing amount", 0m, 5m, null, evidence: false);
        var historyPending = Bill("History pending", 30m, 0m);
        var decrease = Bill("Decreased", 8m, 12m);
        var unchanged = Bill("Unchanged", 8m, 8m);
        var snapshot = BillLedgerSnapshot.Create([
            unchanged, decrease, historyPending, absentAmount, dollarIncrease, euroIncrease
        ]);

        var ordered = snapshot.Select("", BillLedgerFilter.All, BillLedgerSort.Attention);
        Assert.Equal(new[]
        {
            euroIncrease.Id, dollarIncrease.Id,
            absentAmount.Id, historyPending.Id,
            decrease.Id, unchanged.Id
        }, ordered.Select(bill => bill.Id));

        // A filter still has its own meaning; sorting never fabricates a
        // comparison or moves a pending-history bill into "Increased".
        Assert.Equal(new[] { euroIncrease.Id, dollarIncrease.Id },
            snapshot.Select("", BillLedgerFilter.Increased, BillLedgerSort.Attention)
                .Select(bill => bill.Id));
        Assert.Equal(2, snapshot.IncreasedCount);
        Assert.Equal(2, snapshot.Select("", BillLedgerFilter.All, BillLedgerSort.Attention)
            .Count(bill => !BillLedgerSnapshot.CanCompare(bill)));
    }

    [Fact]
    public void SortingTies_UseStableIds()
    {
        var low = Bill("Same") with { Id = Guid.Parse("11111111-1111-1111-1111-111111111111") };
        var high = low with { Id = Guid.Parse("22222222-2222-2222-2222-222222222222") };
        var snapshot = BillLedgerSnapshot.Create([high, low]);
        foreach (var sort in Enum.GetValues<BillLedgerSort>())
            Assert.Equal(low.Id, snapshot.Select(null, BillLedgerFilter.All, sort)[0].Id);
    }

    [Fact]
    public void MissingCadence_PreventsAnnualizedClaimButNotKnownCurrentAmounts()
    {
        var snapshot = BillLedgerSnapshot.Create([Bill(monthly: false)]);
        Assert.Equal("$20.00", snapshot.RecognizedTotal);
        Assert.Equal("$10.00", snapshot.IncreaseTotal);
        Assert.Null(snapshot.AnnualizedIncreaseTotal);
    }

    [Fact]
    public void Rounding_UsesFinancialMidpointAwayFromZero()
    {
        Assert.Equal(.01m, BillLedgerSnapshot.Difference(Bill(current: 10.005m, previous: 10)));
        Assert.Equal(-.01m, BillLedgerSnapshot.Difference(Bill(current: 9.995m, previous: 10)));
    }

    [Fact]
    public void KnownUnchangedAmounts_HaveKnownZeroIncreaseInEachCurrency()
    {
        var snapshot = BillLedgerSnapshot.Create([Bill(previous: 20), Bill(previous: 20, currency: "EUR")]);
        Assert.Equal("EUR 0.00 · $0.00", snapshot.IncreaseTotal);
        Assert.Equal(2, snapshot.UnchangedCount);
        Assert.Null(snapshot.AnnualizedIncreaseTotal);
    }

    [Fact]
    public void EmptySnapshot_IsNotManufacturedZeroEvidence()
    {
        var snapshot = BillLedgerSnapshot.Create([]);
        Assert.Equal(0, snapshot.TotalCount);
        Assert.Null(snapshot.RecognizedTotal);
        Assert.Null(snapshot.IncreaseTotal);
    }

    [Fact]
    public void InvalidIdentityOrDuplicateRecords_RejectWholeSnapshot()
    {
        var bill = Bill();
        Assert.Throws<InvalidOperationException>(() => BillLedgerSnapshot.Create([bill, bill]));
        Assert.Throws<InvalidOperationException>(() => BillLedgerSnapshot.Create([bill with { Id = Guid.Empty }]));
        Assert.Throws<InvalidOperationException>(() => BillLedgerSnapshot.Create([null!]));
    }

    [Fact]
    public void ArithmeticOverflow_IsRejectedInsteadOfMisreportedAsZero() =>
        Assert.Throws<OverflowException>(() => BillLedgerSnapshot.Create([
            Bill(current: decimal.MaxValue, previous: 0),
            Bill(current: decimal.MaxValue, previous: 0)]));
}

using FullWorth.Web.Services;

namespace FullWorth.Tests.Services;

public sealed class ExperienceFocusOrderingTests
{
    [Fact]
    public void NoFocus_PreservesExistingQuickLinkOrder()
    {
        var result =
            ExperienceFocusOrdering.OrderQuickLinks(
                []);

        Assert.Equal(
            [
                "transactions",
                "accounts",
                "activity"
            ],
            result);
    }

    [Fact]
    public void Spending_MovesTransactionsAheadOfOtherDestinations()
    {
        var result =
            ExperienceFocusOrdering.OrderQuickLinks(
                ["Spending"]);

        Assert.Equal(
            "transactions",
            result[0]);
    }

    [Fact]
    public void AccountOverview_MovesAccountsAheadOfOtherDestinations()
    {
        var result =
            ExperienceFocusOrdering.OrderQuickLinks(
                ["AccountOverview"]);

        Assert.Equal(
            "accounts",
            result[0]);
    }

    [Theory]
    [InlineData("BillChanges")]
    [InlineData("RecurringCosts")]
    [InlineData("Statements")]
    public void BillEvidenceFocus_MovesActivityAheadOfOtherDestinations(
        string focus)
    {
        var result =
            ExperienceFocusOrdering.OrderQuickLinks(
                [focus]);

        Assert.Equal(
            "activity",
            result[0]);
    }

    [Fact]
    public void FocusOrdering_NeverRemovesQuickLinks()
    {
        var result =
            ExperienceFocusOrdering.OrderQuickLinks(
                [
                    "BillChanges",
                    "Spending",
                    "RecurringCosts",
                    "AccountOverview",
                    "Statements"
                ]);

        Assert.Equal(
            3,
            result.Distinct(
                StringComparer.Ordinal)
                .Count());

        Assert.Contains(
            "transactions",
            result);

        Assert.Contains(
            "accounts",
            result);

        Assert.Contains(
            "activity",
            result);
    }

    [Fact]
    public void MultipleSelectedAreas_StayDeterministic()
    {
        var result =
            ExperienceFocusOrdering.OrderQuickLinks(
                [
                    "AccountOverview",
                    "BillChanges"
                ]);

        Assert.Equal(
            [
                "accounts",
                "activity",
                "transactions"
            ],
            result);
    }
}

using FullWorth.Core.Models;
using FullWorth.Core.Services;

namespace FullWorth.Tests.Services;

public sealed class BillStreamDiscoveryServiceTests
{
    [Fact]
    public void Discover_PendingPriceChange_DoesNotReplaceLatestPostedCharge()
    {
        var transactions = new[]
        {
            new BankTransaction("Example Subscription", new DateOnly(2026, 1, 12), 20m),
            new BankTransaction("Example Subscription", new DateOnly(2026, 2, 12), 20m),
            new BankTransaction("EXAMPLE SUBSCRIPTION AUTOPAY", new DateOnly(2026, 3, 12), 90m, isPending: true)
        };

        var stream = Assert.Single(new BillStreamDiscoveryService().Discover(transactions));

        Assert.Equal(20m, stream.LatestAmount);
        Assert.Equal(new DateOnly(2026, 2, 12), stream.LatestTransaction!.PostedDate);
        Assert.Equal(2, stream.Transactions.Count);
        Assert.DoesNotContain(stream.Transactions, transaction => transaction.IsPending);
    }

    [Fact]
    public void Discover_PendingChargeBecomesPosted_IncludesItExactlyOnce()
    {
        var transactions = new[]
        {
            new BankTransaction("Example Subscription", new DateOnly(2026, 1, 12), 20m),
            new BankTransaction("Example Subscription", new DateOnly(2026, 2, 12), 20m),
            new BankTransaction("Example Subscription", new DateOnly(2026, 3, 11), 25m, isPending: true),
            new BankTransaction("Example Subscription", new DateOnly(2026, 3, 12), 25m)
        };

        var stream = Assert.Single(new BillStreamDiscoveryService().Discover(transactions));

        Assert.Equal(25m, stream.LatestAmount);
        Assert.Equal(new DateOnly(2026, 3, 12), stream.LatestTransaction!.PostedDate);
        Assert.Equal(3, stream.Transactions.Count);
        Assert.DoesNotContain(stream.Transactions, transaction => transaction.IsPending);
    }

    [Fact]
    public void Discover_OnlyOnePostedCharge_DoesNotUsePendingChargeAsRecurrenceEvidence()
    {
        var transactions = new[]
        {
            new BankTransaction("Example Subscription", new DateOnly(2026, 1, 12), 20m),
            new BankTransaction("Example Subscription", new DateOnly(2026, 2, 12), 20m, isPending: true)
        };

        Assert.Empty(new BillStreamDiscoveryService().Discover(transactions));
    }
}

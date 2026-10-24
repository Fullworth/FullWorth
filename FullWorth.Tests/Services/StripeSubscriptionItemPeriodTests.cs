using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using FullWorth.API.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class StripeSubscriptionItemPeriodTests
{
    private const string MonthlyPrice = "price_period_monthly";
    private const string YearlyPrice = "price_period_yearly";
    private const string SigningSecret = "whsec_synthetic_item_period";
    private static readonly DateTimeOffset Now =
        new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> ItemOrdersAndAccessStates()
    {
        foreach (var yearly in new[] { false, true })
        foreach (var expired in new[] { false, true })
        foreach (var unrelatedFirst in new[] { false, true })
        foreach (var webhook in new[] { false, true })
            yield return [yearly, expired, unrelatedFirst, webhook];
    }

    [Theory]
    [MemberData(nameof(ItemOrdersAndAccessStates))]
    public async Task MatchedProductPeriodControlsAccessAndReconciliation(
        bool yearly, bool expired, bool unrelatedFirst, bool webhook)
    {
        var start = Now.AddDays(expired ? -60 : -5);
        var end = Now.AddDays(expired ? -1 : 25);
        var matching = Item(yearly ? YearlyPrice : MonthlyPrice, start, end);
        // Give the unrelated product the opposite access outcome.
        var unrelated = Item("price_unrelated", Now.AddDays(-365),
            Now.AddDays(expired ? 365 : -10));
        var items = unrelatedFirst ? new[] { unrelated, matching } : new[] { matching, unrelated };
        using var fixture = await CreateAsync(Subscription(items));

        var state = await fixture.Billing.GetCurrentSubscriptionAsync(
            fixture.UserId, null, CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal(start, state.CurrentPeriodStartUtc);
        Assert.Equal(end, state.CurrentPeriodEndUtc);
        Assert.Equal(yearly ? "yearly" : "monthly", state.BillingInterval);
        Assert.Equal(!expired, state.IsEntitled(Now));

        await ReconcileAsync(fixture, webhook);
        var entitlement = await fixture.Db.SubscriptionEntitlements.SingleAsync();
        Assert.Equal(expired, entitlement.IsRevoked);
        if (!expired)
        {
            Assert.Equal(start, entitlement.StartsAtUtc);
            Assert.Equal(end, entitlement.EndsAtUtc);
        }
        Assert.Equal(fixture.UserId, entitlement.UserId);
        Assert.Equal(webhook ? 1 : 0, await fixture.Db.StripeWebhookEvents.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingMatchedPeriodCannotBorrowUnrelatedDates(bool webhook)
    {
        using var fixture = await CreateAsync(Subscription([
            Item("price_unrelated", Now.AddDays(-5), Now.AddYears(2)),
            Item(MonthlyPrice)
        ]));

        var state = await fixture.Billing.GetCurrentSubscriptionAsync(
            fixture.UserId, null, CancellationToken.None);
        Assert.NotNull(state);
        Assert.True(state.IsFullWorthPlan);
        Assert.Null(state.CurrentPeriodStartUtc);
        Assert.Null(state.CurrentPeriodEndUtc);
        Assert.False(state.IsEntitled(Now));

        await ReconcileAsync(fixture, webhook);
        Assert.True((await fixture.Db.SubscriptionEntitlements.SingleAsync()).IsRevoked);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRootPeriodsRemainFallbackOnlyForTheMatchedProduct(bool webhook)
    {
        var start = Now.AddDays(-5);
        var end = Now.AddDays(25);
        using var fixture = await CreateAsync(Subscription([
            Item("price_unrelated", Now.AddYears(-1), Now.AddYears(2)),
            Item(YearlyPrice)
        ], start, end));

        var state = await fixture.Billing.GetCurrentSubscriptionAsync(
            fixture.UserId, null, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(start, state.CurrentPeriodStartUtc);
        Assert.Equal(end, state.CurrentPeriodEndUtc);
        await ReconcileAsync(fixture, webhook);
        var entitlement = await fixture.Db.SubscriptionEntitlements.SingleAsync();
        Assert.False(entitlement.IsRevoked);
        Assert.Equal(start, entitlement.StartsAtUtc);
        Assert.Equal(end, entitlement.EndsAtUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchedItemPeriodTakesPrecedenceOverAggregateRootDates(bool webhook)
    {
        var start = Now.AddDays(-3);
        var end = Now.AddDays(27);
        using var fixture = await CreateAsync(Subscription([
            Item(MonthlyPrice, start, end)
        ], Now.AddYears(-1), Now.AddYears(2)));

        var state = await fixture.Billing.GetCurrentSubscriptionAsync(
            fixture.UserId, null, CancellationToken.None);
        Assert.NotNull(state);
        Assert.Equal(start, state.CurrentPeriodStartUtc);
        Assert.Equal(end, state.CurrentPeriodEndUtc);
        await ReconcileAsync(fixture, webhook);
        var entitlement = await fixture.Db.SubscriptionEntitlements.SingleAsync();
        Assert.Equal(start, entitlement.StartsAtUtc);
        Assert.Equal(end, entitlement.EndsAtUtc);
    }

    private static Dictionary<string, object> Item(
        string price, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var item = new Dictionary<string, object> { ["price"] = new { id = price } };
        if (start is not null) item["current_period_start"] = start.Value.ToUnixTimeSeconds();
        if (end is not null) item["current_period_end"] = end.Value.ToUnixTimeSeconds();
        return item;
    }

    private static Dictionary<string, object> Subscription(
        Dictionary<string, object>[] items, DateTimeOffset? start = null, DateTimeOffset? end = null)
    {
        var subscription = new Dictionary<string, object>
        {
            ["id"] = "sub_synthetic_period",
            ["status"] = "active",
            ["items"] = new { data = items }
        };
        if (start is not null) subscription["current_period_start"] = start.Value.ToUnixTimeSeconds();
        if (end is not null) subscription["current_period_end"] = end.Value.ToUnixTimeSeconds();
        return subscription;
    }

    private static async Task ReconcileAsync(Fixture fixture, bool webhook)
    {
        if (!webhook)
        {
            await fixture.Billing.SyncCurrentSubscriptionAsync(
                fixture.UserId, null, CancellationToken.None);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            id = "evt_syntheticitemperiod",
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    subscription = "sub_synthetic_period",
                    metadata = new Dictionary<string, string>
                    {
                        ["billwatch_user_id"] = fixture.UserId.ToString("D")
                    }
                }
            }
        });
        var timestamp = Now.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(SigningSecret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();
        await fixture.Billing.HandleWebhookAsync(
            payload, $"t={timestamp},v1={signature}", CancellationToken.None);
    }

    private static async Task<Fixture> CreateAsync(Dictionary<string, object> subscription)
    {
        var userId = Guid.NewGuid();
        var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        db.Users.Add(new ApplicationUser { Id = userId, UserName = userId.ToString("N") });
        db.SubscriptionEntitlements.Add(new SubscriptionEntitlementEntity
        {
            UserId = userId,
            Tier = FullWorthSubscriptionTier.Standard,
            Source = SubscriptionEntitlementSource.Paid,
            StartsAtUtc = Now.AddDays(-90),
            EndsAtUtc = Now.AddYears(1),
            CreatedAtUtc = Now.AddDays(-90),
            UpdatedAtUtc = Now.AddDays(-90)
        });
        await db.SaveChangesAsync();
        var client = new HttpClient(new Handler(request => request.RequestUri!.AbsolutePath switch
        {
            "/v1/customers/search" => JsonSerializer.Serialize(new
            {
                data = new[] { new { id = "cus_synthetic_period", metadata =
                    new Dictionary<string, string> { ["billwatch_user_id"] = userId.ToString("D") } } }
            }),
            "/v1/subscriptions" => JsonSerializer.Serialize(new { data = new[] { subscription } }),
            "/v1/subscriptions/sub_synthetic_period" => JsonSerializer.Serialize(subscription),
            _ => throw new InvalidOperationException("Unexpected provider request.")
        }));
        var billing = new StripeBillingService(client, new StripeBillingOptions
        {
            Enabled = true,
            SecretKey = "sk_test_synthetic_period",
            WebhookSecret = SigningSecret,
            MonthlyPriceId = MonthlyPrice,
            YearlyPriceId = YearlyPrice,
            PublicWebBaseUrl = "https://fullworth.org"
        }, db, new IdentityUserExistenceGateway(db), new FixedTimeProvider());
        return new Fixture(userId, billing, db, client);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler(Func<HttpRequestMessage, string> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
    }

    private sealed record Fixture(
        Guid UserId, StripeBillingService Billing, FullWorthDbContext Db, HttpClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Db.Dispose();
        }
    }
}

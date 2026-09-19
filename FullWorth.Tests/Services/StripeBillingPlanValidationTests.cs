using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class StripeBillingPlanValidationTests
{
    private const string MonthlyPriceId = "price_billwatch_monthly_test";
    private const string YearlyPriceId = "price_billwatch_yearly_test";

    [Theory]
    [InlineData("price_other_product", false)]
    [InlineData(MonthlyPriceId, true)]
    [InlineData(YearlyPriceId, true)]
    public async Task CurrentSubscription_RecognizesOnlyConfiguredFullWorthPrices(
        string priceId,
        bool expected)
    {
        var userId = Guid.NewGuid();
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();

        using var service = CreateService(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/v1/customers/search" =>
                    JsonSerializer.Serialize(new
                    {
                        data = new[]
                        {
                            new
                            {
                                id = "cus_test",
                                metadata = new Dictionary<string, string>
                                {
                                    ["billwatch_user_id"] = userId.ToString("D")
                                }
                            }
                        }
                    }),
                "/v1/subscriptions" =>
                    JsonSerializer.Serialize(new
                    {
                        data = new[]
                        {
                            new
                            {
                                status = "active",
                                current_period_end = periodEnd,
                                items = new
                                {
                                    data = new[] { new { price = new { id = priceId } } }
                                }
                            }
                        }
                    }),
                _ => throw new InvalidOperationException("Unexpected Stripe request.")
            });

        var state = await service.Billing.GetCurrentSubscriptionAsync(
            userId,
            "person@example.com",
            CancellationToken.None);

        Assert.Equal(expected, state?.IsEntitled(DateTimeOffset.UtcNow) == true);
        Assert.Equal(expected, state is not null);
    }

    [Theory]
    [InlineData("month", true)]
    [InlineData("year", false)]
    public async Task ConfiguredMonthlyPrice_MustBeAnActiveMonthlyRecurringPrice(
        string providerInterval,
        bool expectedValid)
    {
        using var service = CreateService(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/v1/prices/price_billwatch_monthly_test" =>
                    JsonSerializer.Serialize(new
                    {
                        id = MonthlyPriceId,
                        active = true,
                        unit_amount = 900,
                        currency = "usd",
                        recurring = new { interval = providerInterval }
                    }),
                "/v1/prices/price_billwatch_yearly_test" =>
                    JsonSerializer.Serialize(new
                    {
                        id = YearlyPriceId,
                        active = true,
                        unit_amount = 9000,
                        currency = "usd",
                        recurring = new { interval = "year" }
                    }),
                _ => throw new InvalidOperationException("Unexpected Stripe request.")
            });

        if (expectedValid)
        {
            var plans = await service.Billing.GetPlansAsync(CancellationToken.None);
            Assert.Collection(
                plans,
                monthly => Assert.Equal("monthly", monthly.BillingInterval),
                yearly => Assert.Equal("yearly", yearly.BillingInterval));
        }
        else
        {
            await Assert.ThrowsAsync<StripeBillingException>(
                () => service.Billing.GetPlansAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Checkout_RejectsMisconfiguredPriceBeforeCreatingCustomer()
    {
        using var service = CreateService(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/v1/prices/price_billwatch_monthly_test" =>
                    JsonSerializer.Serialize(new
                    {
                        id = MonthlyPriceId,
                        active = false,
                        unit_amount = 900,
                        currency = "usd",
                        recurring = new { interval = "month" }
                    }),
                _ => throw new InvalidOperationException(
                    "Checkout must stop before creating a customer or session.")
            });

        await Assert.ThrowsAsync<StripeBillingException>(
            () => service.Billing.CreateCheckoutUrlAsync(
                Guid.NewGuid(),
                "person@example.com",
                "monthly",
                CancellationToken.None));
    }

    [Fact]
    public async Task DelayedActiveWebhook_ReconcilesCurrentCanceledState()
    {
        var userId = Guid.NewGuid();
        var periodEnd = DateTimeOffset.UtcNow.AddDays(30).ToUnixTimeSeconds();

        using var service = CreateService(
            request => request.RequestUri!.AbsolutePath switch
            {
                "/v1/customers/search" =>
                    JsonSerializer.Serialize(new
                    {
                        data = new[]
                        {
                            new
                            {
                                id = "cus_test",
                                metadata = new Dictionary<string, string>
                                {
                                    ["billwatch_user_id"] = userId.ToString("D")
                                }
                            }
                        }
                    }),
                "/v1/subscriptions" =>
                    JsonSerializer.Serialize(new
                    {
                        data = new[]
                        {
                            new
                            {
                                status = "canceled",
                                current_period_end = periodEnd,
                                items = new
                                {
                                    data = new[]
                                    {
                                        new { price = new { id = MonthlyPriceId } }
                                    }
                                }
                            }
                        }
                    }),
                _ => throw new InvalidOperationException("Unexpected Stripe request.")
            });

        service.Db.Users.Add(new ApplicationUser
        {
            Id = userId,
            UserName = "person@example.com",
            Email = "person@example.com"
        });
        service.Db.SubscriptionEntitlements.Add(new SubscriptionEntitlementEntity
        {
            UserId = userId,
            Tier = FullWorthSubscriptionTier.Standard,
            Source = SubscriptionEntitlementSource.Paid,
            StartsAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            EndsAtUtc = DateTimeOffset.UtcNow.AddDays(30),
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1)
        });
        await service.Db.SaveChangesAsync();

        var payload = JsonSerializer.Serialize(new
        {
            type = "customer.subscription.updated",
            data = new
            {
                @object = new
                {
                    status = "active",
                    metadata = new Dictionary<string, string>
                    {
                        ["billwatch_user_id"] = userId.ToString("D")
                    }
                }
            }
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("whsec_billwatch_test"),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

        await service.Billing.HandleWebhookAsync(
            payload,
            $"t={timestamp},v1={signature}",
            CancellationToken.None);

        var entitlement = await service.Db.SubscriptionEntitlements.SingleAsync();
        Assert.True(entitlement.IsRevoked);
    }

    [Fact]
    public async Task ProviderNetworkFailure_UsesControlledBillingError()
    {
        using var service = CreateService(
            _ => throw new HttpRequestException("provider unavailable"));

        var exception = await Assert.ThrowsAsync<StripeBillingException>(
            () => service.Billing.GetPlansAsync(CancellationToken.None));

        Assert.DoesNotContain(
            "provider unavailable",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static TestService CreateService(Func<HttpRequestMessage, string> respond)
    {
        var client = new HttpClient(new StubHandler(respond));
        var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
        var options = new StripeBillingOptions
        {
            Enabled = true,
            SecretKey = "sk_test_billwatch",
            WebhookSecret = "whsec_billwatch_test",
            MonthlyPriceId = MonthlyPriceId,
            YearlyPriceId = YearlyPriceId,
            PublicWebBaseUrl = "https://billbeacon.net"
        };

        return new TestService(
            new StripeBillingService(client, options, db, TimeProvider.System),
            client,
            db);
    }

    private sealed record TestService(
        StripeBillingService Billing,
        HttpClient Client,
        FullWorthDbContext Db) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Db.Dispose();
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, string> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(respond(request), Encoding.UTF8, "application/json")
            });
        }
    }
}

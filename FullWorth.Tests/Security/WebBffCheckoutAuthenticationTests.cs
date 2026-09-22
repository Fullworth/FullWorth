using System.Net;
using System.Net.Http.Json;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebBffCheckoutAuthenticationTests
{
    [Fact]
    public async Task Checkout_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");

        using var response = await client.PostAsJsonAsync(
            "/bff/subscription/checkout",
            new { billingInterval = "monthly" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_ValidRequest_IsAllowedThroughWriteProxyBoundary()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Test-UserId",
            "11111111-1111-1111-1111-111111111111");

        using var antiforgeryResponse = await client.GetAsync("/bff/antiforgery");
        antiforgeryResponse.EnsureSuccessStatusCode();

        var antiforgery = await antiforgeryResponse.Content
            .ReadFromJsonAsync<AntiforgeryPayload>();

        Assert.NotNull(antiforgery);
        Assert.False(string.IsNullOrWhiteSpace(antiforgery!.RequestToken));

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/bff/subscription/checkout")
        {
            Content = JsonContent.Create(new { billingInterval = "monthly" })
        };
        request.Headers.Add("X-CSRF-TOKEN", antiforgery.RequestToken);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record AntiforgeryPayload(string RequestToken);
}

using System.Net;
using System.Net.Http.Json;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffCheckoutAuthenticationTests
{
    [Fact] public async Task Checkout_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsJsonAsync("/bff/subscription/checkout", new { billingInterval = "monthly" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffBillingPortalAuthenticationTests
{
    [Fact] public async Task BillingPortal_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsync("/bff/subscription/billing-portal", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

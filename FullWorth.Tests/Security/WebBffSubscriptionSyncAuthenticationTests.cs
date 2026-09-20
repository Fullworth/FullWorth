using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffSubscriptionSyncAuthenticationTests
{
    [Fact] public async Task Sync_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsync("/bff/subscription/sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffPlaidUpdateAuthenticationTests
{
    [Fact] public async Task UpdateLinkSession_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsync($"/bff/plaid/connections/{Guid.NewGuid()}/update-link-session", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

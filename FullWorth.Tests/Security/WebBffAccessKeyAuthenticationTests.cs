using System.Net;
using System.Net.Http.Json;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffAccessKeyAuthenticationTests
{
    [Fact] public async Task Redeem_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsJsonAsync("/bff/subscription/access-keys/redeem", new { accessKey = "invalid" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

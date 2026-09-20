using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class BillDiscoveryAuthorizationTests
{
    [Fact]
    public async Task RunDiscovery_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/api/bill-discovery/run",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

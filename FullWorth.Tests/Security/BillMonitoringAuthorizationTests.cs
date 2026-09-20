using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class BillMonitoringAuthorizationTests
{
    [Fact]
    public async Task Refresh_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/api/bill-monitoring/refresh",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebBffAuthenticationBoundaryTests
{
    [Theory]
    [InlineData("/bff/antiforgery")]
    [InlineData("/bff/subscription")]
    [InlineData("/bff/subscription/plans")]
    [InlineData("/bff/bill-streams")]
    [InlineData("/bff/bank-accounts")]
    [InlineData("/bff/bank-connections")]
    [InlineData("/bff/bank-transactions")]
    [InlineData("/bff/alerts")]
    [InlineData("/bff/account/export")]
    public async Task BffReads_RequireAuthenticatedSession(string route)
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MonitoringRefresh_RequiresAuthenticatedSession()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");

        using var response =
            await client.PostAsync(
                "/bff/bill-monitoring/refresh",
                content: null);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }
}

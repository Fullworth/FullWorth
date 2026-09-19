using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class ApiSecurityHeaderTests
{
    [Theory]
    [InlineData("/api/bank-accounts")]
    [InlineData("/api/bill-streams")]
    [InlineData("/api/account/preferences")]
    public async Task ProtectedResponses_IncludeBrowserHardeningHeaders(string route)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertHeader(response, "X-Content-Type-Options", "nosniff");
        AssertHeader(response, "X-Frame-Options", "DENY");
        AssertHeader(response, "Referrer-Policy", "no-referrer");
    }

    private static void AssertHeader(
        HttpResponseMessage response,
        string name,
        string expectedValue)
    {
        Assert.True(response.Headers.TryGetValues(name, out var values));
        Assert.Contains(values, value =>
            string.Equals(value, expectedValue, StringComparison.OrdinalIgnoreCase));
    }
}

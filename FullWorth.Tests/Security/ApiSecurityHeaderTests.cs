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

    [Fact]
    public async Task Responses_UseServerGeneratedRequestIdInsteadOfClientValue()
    {
        const string spoofedRequestId =
            "attacker-controlled-request-id";

        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Request-Id",
            spoofedRequestId);

        using var firstResponse =
            await client.GetAsync(
                "/api/bank-accounts");

        using var secondResponse =
            await client.GetAsync(
                "/api/bank-accounts");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            firstResponse.StatusCode);

        var firstRequestId =
            Assert.Single(
                firstResponse.Headers.GetValues(
                    "X-FullWorth-Request-Id"));

        var secondRequestId =
            Assert.Single(
                secondResponse.Headers.GetValues(
                    "X-FullWorth-Request-Id"));

        Assert.Matches(
            "^[0-9a-f]{32}$",
            firstRequestId);

        Assert.Matches(
            "^[0-9a-f]{32}$",
            secondRequestId);

        Assert.NotEqual(
            spoofedRequestId,
            firstRequestId);

        Assert.NotEqual(
            firstRequestId,
            secondRequestId);
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

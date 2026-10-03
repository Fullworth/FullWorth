using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class FinancialOperationRateLimitTests
{
    [Theory]
    [InlineData(
        "/api/plaid/connections/10000000-0000-0000-0000-000000000001/accounts/sync",
        6)]
    [InlineData(
        "/api/plaid/exchange-public-token",
        20)]
    public async Task ApiFinancialPolicies_EnforcePerUserBudgets(
        string path,
        int permitLimit)
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        await AssertPerUserLimitAsync(
            client,
            path,
            permitLimit);
    }

    [Theory]
    [InlineData(
        "/bff/bill-monitoring/refresh",
        6)]
    [InlineData(
        "/bff/plaid/link-session/10000000-0000-0000-0000-000000000001/complete",
        20)]
    public async Task WebFinancialPolicies_EnforcePerUserBudgets(
        string path,
        int permitLimit)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        await AssertPerUserLimitAsync(
            client,
            path,
            permitLimit);
    }

    private static async Task AssertPerUserLimitAsync(
        HttpClient client,
        string path,
        int permitLimit)
    {
        var firstUserId =
            Guid.NewGuid();

        for (var attempt = 1;
             attempt <= permitLimit;
             attempt++)
        {
            using var response =
                await client.SendAsync(
                    CreateRequest(
                        path,
                        firstUserId));

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.SendAsync(
                CreateRequest(
                    path,
                    firstUserId));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limitedResponse.StatusCode);

        Assert.True(
            limitedResponse.Headers.TryGetValues(
                "Retry-After",
                out var retryAfterValues));

        Assert.True(
            int.TryParse(
                Assert.Single(
                    retryAfterValues),
                out var retryAfterSeconds));

        Assert.True(
            retryAfterSeconds >
            0);

        using var secondUserResponse =
            await client.SendAsync(
                CreateRequest(
                    path,
                    Guid.NewGuid()));

        Assert.NotEqual(
            HttpStatusCode.TooManyRequests,
            secondUserResponse.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(
        string path,
        Guid userId)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                path);

        request.Headers.Add(
            "X-FullWorth-Test-UserId",
            userId.ToString(
                "D"));

        return request;
    }
}

using System.Net;
using System.Net.Http.Headers;
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

        var firstUser =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        for (var attempt = 1;
             attempt <= permitLimit;
             attempt++)
        {
            using var response =
                await client.SendAsync(
                    CreateApiRequest(
                        path,
                        firstUser.AccessToken));

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.SendAsync(
                CreateApiRequest(
                    path,
                    firstUser.AccessToken));

        AssertRateLimited(
            limitedResponse);

        var secondUser =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        using var secondUserResponse =
            await client.SendAsync(
                CreateApiRequest(
                    path,
                    secondUser.AccessToken));

        Assert.NotEqual(
            HttpStatusCode.TooManyRequests,
            secondUserResponse.StatusCode);
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

        var firstUserId =
            Guid.NewGuid();

        for (var attempt = 1;
             attempt <= permitLimit;
             attempt++)
        {
            using var response =
                await client.SendAsync(
                    CreateWebRequest(
                        path,
                        firstUserId));

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.SendAsync(
                CreateWebRequest(
                    path,
                    firstUserId));

        AssertRateLimited(
            limitedResponse);

        using var secondUserResponse =
            await client.SendAsync(
                CreateWebRequest(
                    path,
                    Guid.NewGuid()));

        Assert.NotEqual(
            HttpStatusCode.TooManyRequests,
            secondUserResponse.StatusCode);
    }

    private static void AssertRateLimited(
        HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            response.StatusCode);

        Assert.True(
            response.Headers.TryGetValues(
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
    }

    private static HttpRequestMessage CreateApiRequest(
        string path,
        string accessToken)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                path);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        return request;
    }

    private static HttpRequestMessage CreateWebRequest(
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

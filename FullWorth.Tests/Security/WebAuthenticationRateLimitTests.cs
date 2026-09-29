using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebAuthenticationRateLimitTests
{
    private const int RequestsPerWindow =
        20;

    [Theory]
    [InlineData("/auth/login")]
    [InlineData("/auth/register")]
    [InlineData("/auth/forgot-password")]
    [InlineData("/auth/reset-password")]
    [InlineData("/auth/external/two-factor")]
    [InlineData("/auth/external/register/complete")]
    public async Task AnonymousAuthenticationPosts_AreIpRateLimited(
        string path)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        for (var attempt = 1;
             attempt <= RequestsPerWindow;
             attempt++)
        {
            using var response =
                await client.SendAsync(
                    CreateRequest(path));

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.SendAsync(
                CreateRequest(path));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limitedResponse.StatusCode);
    }

    private static HttpRequestMessage CreateRequest(
        string path)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                path);

        // The auth limiter must use the caller's IP, not a claimed account ID.
        request.Headers.Add(
            "X-FullWorth-Test-UserId",
            Guid.NewGuid().ToString("D"));

        return request;
    }
}

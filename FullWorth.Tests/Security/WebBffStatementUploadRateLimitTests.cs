using System.Net;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebBffStatementUploadRateLimitTests
{
    private const int UploadLimit =
        12;

    [Fact]
    public async Task StatementUploadLimiter_IsPartitionedByUser()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        var firstUserId =
            Guid.NewGuid();

        for (var attempt = 1;
             attempt <= UploadLimit;
             attempt++)
        {
            using var response =
                await client.SendAsync(
                    CreateUploadRequest(
                        firstUserId));

            Assert.NotEqual(
                HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }

        using var limitedResponse =
            await client.SendAsync(
                CreateUploadRequest(
                    firstUserId));

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            limitedResponse.StatusCode);

        var secondUserId =
            Guid.NewGuid();

        using var secondUserResponse =
            await client.SendAsync(
                CreateUploadRequest(
                    secondUserId));

        Assert.NotEqual(
            HttpStatusCode.TooManyRequests,
            secondUserResponse.StatusCode);
    }

    private static HttpRequestMessage CreateUploadRequest(
        Guid userId)
    {
        var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                $"/bff/bill-streams/{Guid.NewGuid():D}/statement-uploads");

        request.Headers.Add(
            "X-FullWorth-Test-UserId",
            userId.ToString("D"));

        return request;
    }
}

using System.Net;
using System.Text;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebCrossOriginRequestBoundaryTests
{
    [Fact]
    public async Task UnsafeWebRequest_RejectsCrossSiteFetchMetadata()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateUnsafeBffRequest();

        request.Headers.TryAddWithoutValidation(
            "Sec-Fetch-Site",
            "cross-site");

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task UnsafeWebRequest_RejectsMismatchedOrigin()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateUnsafeBffRequest();

        request.Headers.TryAddWithoutValidation(
            "Origin",
            "https://attacker.example");

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task UnsafeWebRequest_SameOriginStillRequiresAntiforgeryToken()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateUnsafeBffRequest();

        request.Headers.TryAddWithoutValidation(
            "Origin",
            "https://localhost");

        request.Headers.TryAddWithoutValidation(
            "Sec-Fetch-Site",
            "same-origin");

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task UnsafeWebRequest_OpaqueOriginWithSameOriginMetadataStillRequiresAntiforgery()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateUnsafeBffRequest();

        request.Headers.TryAddWithoutValidation(
            "Origin",
            "null");

        request.Headers.TryAddWithoutValidation(
            "Sec-Fetch-Site",
            "same-origin");

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("cross-site")]
    public async Task UnsafeWebRequest_OpaqueOriginWithoutSameOriginMetadataIsRejected(
        string? fetchSite)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            CreateUnsafeBffRequest();

        request.Headers.TryAddWithoutValidation(
            "Origin",
            "null");

        if (fetchSite is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "Sec-Fetch-Site",
                fetchSite);
        }

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    [Fact]
    public async Task SafeWebGet_IsNotBlockedByCrossSiteFetchMetadata()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Get,
                "/auth/confirm-email");

        request.Headers.TryAddWithoutValidation(
            "Sec-Fetch-Site",
            "cross-site");

        using var response =
            await client.SendAsync(request);

        Assert.NotEqual(
            HttpStatusCode.Forbidden,
            response.StatusCode);
    }

    private static HttpRequestMessage CreateUnsafeBffRequest()
    {
        return new HttpRequestMessage(
            HttpMethod.Post,
            "/bff/account/export")
        {
            Content = new StringContent(
                "{}",
                Encoding.UTF8,
                "application/json")
        };
    }
}

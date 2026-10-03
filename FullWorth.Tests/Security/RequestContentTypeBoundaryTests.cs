using System.Net;
using System.Text;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class RequestContentTypeBoundaryTests
{
    [Fact]
    public async Task ApiBodyBearingMutation_RejectsMissingContentType()
    {
        using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        using var content = new ByteArrayContent(
            Encoding.UTF8.GetBytes("{}"));

        Assert.Null(content.Headers.ContentType);

        using var response = await client.PostAsync(
            "/api/auth/login",
            content);

        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
    }

    [Fact]
    public async Task ApiBodyBearingMutation_RejectsTextPlain()
    {
        using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/api/auth/login",
            new StringContent(
                "{}",
                Encoding.UTF8,
                "text/plain"));

        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/merge-patch+json")]
    public async Task ApiBodyBearingMutation_AllowsJsonMediaTypes(
        string mediaType)
    {
        using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/api/auth/login",
            new StringContent(
                "{}",
                Encoding.UTF8,
                mediaType));

        Assert.NotEqual(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
    }

    [Fact]
    public async Task ApiBodylessMutation_DoesNotRequireContentType()
    {
        using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/account/export");

        using var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task WebBodyBearingMutation_RejectsTextPlain()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PostAsync(
            "/auth/login",
            new StringContent(
                "{}",
                Encoding.UTF8,
                "text/plain"));

        Assert.Equal(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
    }

    [Fact]
    public async Task WebFormMutation_ReachesAntiforgeryBoundary()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["email"] = "person@example.com"
            });

        using var response = await client.PostAsync(
            "/auth/login",
            content);

        Assert.NotEqual(
            HttpStatusCode.UnsupportedMediaType,
            response.StatusCode);
    }
}

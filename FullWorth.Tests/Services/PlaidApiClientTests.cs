using System.Net;
using System.Text;
using FullWorth.API.Services.Plaid;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidApiClientTests
{
    [Fact]
    public async Task PostAsync_SendsCredentialsOnlyToConfiguredPlaidOrigin()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    JsonResponse(
                        HttpStatusCode.OK,
                        "{\"request_id\":\"request-1\"}"));

        var client =
            CreateClient(
                handler);

        using var response =
            await client.PostAsync(
                "accounts/get",
                new
                {
                    access_token =
                        "access-test-token"
                });

        Assert.Equal(
            new Uri(
                "https://sandbox.plaid.com/accounts/get"),
            handler.RequestUri);

        Assert.Equal(
            "test-client-id",
            handler.ClientId);

        Assert.Equal(
            "test-secret",
            handler.Secret);

        Assert.Contains(
            "access-test-token",
            handler.RequestBody,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "test-secret",
            handler.RequestBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostAsync_RejectsAbsoluteEndpointBeforeSendingRequest()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    throw new InvalidOperationException(
                        "The request must not be sent."));

        var client =
            CreateClient(
                handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                client.PostAsync(
                    "https://attacker.invalid/collect",
                    new { value = 1 }));

        Assert.Equal(
            0,
            handler.RequestCount);
    }

    [Fact]
    public async Task PostAsync_RejectsOversizedProviderResponse()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    new HttpResponseMessage(
                        HttpStatusCode.OK)
                    {
                        Content =
                            new ByteArrayContent(
                                new byte[
                                    (5 * 1024 * 1024) + 1])
                    });

        var exception =
            await Assert.ThrowsAsync<PlaidApiException>(
                () =>
                    CreateClient(
                            handler)
                        .PostAsync(
                            "accounts/get",
                            new { }));

        Assert.Equal(
            "RESPONSE_TOO_LARGE",
            exception.ErrorCode);
    }

    [Fact]
    public async Task PostAsync_SanitizesProviderErrorMetadata()
    {
        var handler =
            new RecordingHandler(
                _ =>
                    JsonResponse(
                        HttpStatusCode.BadRequest,
                        "{\"error_type\":\"ITEM_ERROR\\r\\n\",\"error_code\":\"ITEM_NOT_FOUND<script>\",\"request_id\":\"request-1\\nforged\"}"));

        var exception =
            await Assert.ThrowsAsync<PlaidApiException>(
                () =>
                    CreateClient(
                            handler)
                        .PostAsync(
                            "item/get",
                            new { }));

        Assert.Equal(
            "ITEM_ERROR",
            exception.ErrorType);

        Assert.DoesNotContain(
            '<',
            exception.Message);

        Assert.DoesNotContain(
            '\n',
            exception.Message);

        Assert.DoesNotContain(
            '\r',
            exception.Message);
    }

    [Fact]
    public void ProductionPlaidTransport_DoesNotFollowRedirectsOrHandleCookies()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(
            PlaidHttpClientRegistration.CreatePrimaryHandler());

        // HttpClient's default handler permits redirects, which may carry
        // custom provider authentication headers to a different origin.
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.TemporaryRedirect)]
    [InlineData(HttpStatusCode.PermanentRedirect)]
    public async Task PostAsync_RedirectResponseIsError_NotProviderSuccess(
        HttpStatusCode redirectStatus)
    {
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(redirectStatus);
            response.Headers.Location =
                new Uri("https://untrusted.invalid/collect?hint=private");
            return response;
        });

        var error = await Assert.ThrowsAsync<PlaidApiException>(
            () => CreateClient(handler).PostAsync(
                "item/get", new { access_token = "private-fixture-access" }));

        Assert.Equal(redirectStatus, error.StatusCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(new Uri("https://sandbox.plaid.com/item/get"),
            handler.RequestUri);
        Assert.DoesNotContain("untrusted.invalid", error.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-fixture-access", error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("7")]
    [InlineData("\"untrusted error body\"")]
    [InlineData("{\"error_type\":[\"bad\"],\"error_code\":{\"nested\":true}}")]
    public async Task PostAsync_NonObjectOrUnexpectedErrorShape_ProducesSanitizedProviderException(
        string response)
    {
        var handler = new RecordingHandler(_ =>
            JsonResponse(HttpStatusCode.BadGateway, response));

        var error = await Assert.ThrowsAsync<PlaidApiException>(
            () => CreateClient(handler).PostAsync(
                "item/get", new { access_token = "private-fixture-access" }));

        Assert.Equal(HttpStatusCode.BadGateway, error.StatusCode);
        Assert.Null(error.ErrorCode);
        Assert.Null(error.ErrorType);
        Assert.Equal(1, handler.RequestCount);
        Assert.DoesNotContain("private-fixture-access", error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain("untrusted error body", error.Message,
            StringComparison.Ordinal);
    }

    private static PlaidApiClient CreateClient(
        HttpMessageHandler handler)
    {
        return new PlaidApiClient(
            new HttpClient(
                handler),
            Options.Create(
                new PlaidOptions
                {
                    ClientId =
                        "test-client-id",

                    Secret =
                        "test-secret",

                    Environment =
                        PlaidOptions.SandboxEnvironment
                }));
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(
            statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? ClientId { get; private set; }

        public string? Secret { get; private set; }

        public string RequestBody { get; private set; } =
            string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;

            RequestUri =
                request.RequestUri;

            ClientId =
                request.Headers.TryGetValues(
                    "PLAID-CLIENT-ID",
                    out var clientIds)
                    ? Assert.Single(
                        clientIds)
                    : null;

            Secret =
                request.Headers.TryGetValues(
                    "PLAID-SECRET",
                    out var secrets)
                    ? Assert.Single(
                        secrets)
                    : null;

            RequestBody =
                request.Content is null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(
                        cancellationToken);

            return responseFactory(
                request);
        }
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using FullWorth.API.Services.Statements;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FullWorth.Tests.Services;

public sealed class ParserWorkerBillStatementOcrEngineTests
{
    [Fact]
    public void SendsAuthenticatedBodyBoundRequestAndAcceptsBoundedResult()
    {
        var payload = Encoding.UTF8.GetBytes("synthetic-image");
        using var client = new HttpClient(new RecordingHandler(payload))
        {
            BaseAddress = new Uri("https://parser-worker.test")
        };
        var engine = new ParserWorkerBillStatementOcrEngine(
            client,
            new ParserWorkerAuthenticationOptions(
                ParserWorkerAuthenticationOptions.DevelopmentToken,
                isDevelopment: true),
            NullLogger<ParserWorkerBillStatementOcrEngine>.Instance);

        var result = engine.TryExtract(
            new MemoryStream(payload),
            "image/png",
            ".png");

        Assert.True(result.IsUsable);
        Assert.Equal("recognized total 42.00", result.Text);
        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void FailsClosedForOversizedInputWithoutSending()
    {
        var handler = new RecordingHandler([]);
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://parser-worker.test")
        };
        var engine = new ParserWorkerBillStatementOcrEngine(
            client,
            new ParserWorkerAuthenticationOptions(
                ParserWorkerAuthenticationOptions.DevelopmentToken,
                isDevelopment: true),
            NullLogger<ParserWorkerBillStatementOcrEngine>.Instance);

        var result = engine.TryExtract(
            new MemoryStream(new byte[(15 * 1024 * 1024) + 1]),
            "image/png",
            ".png");

        Assert.False(result.IsUsable);
        Assert.Equal(0, handler.RequestCount);
    }

    private sealed class RecordingHandler(byte[] expectedBody) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal("/v1/ocr/extract", request.RequestUri?.AbsolutePath);
            Assert.Equal("image/png", request.Content?.Headers.ContentType?.MediaType);
            Assert.Equal(".png", Assert.Single(request.Headers.GetValues("X-FullWorth-Ocr-Extension")));
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var actualBody = request.Content!\n                .ReadAsByteArrayAsync(cancellationToken)\n                .GetAwaiter()\n                .GetResult();
            Assert.Equal(expectedBody, actualBody);
            var expectedHash = Convert.ToHexString(SHA256.HashData(actualBody)).ToLowerInvariant();
            Assert.Equal(
                expectedHash,
                Assert.Single(request.Headers.GetValues(
                    ParserWorkerAuthenticationOptions.ContentSha256HeaderName)));
            Assert.NotEmpty(request.Headers.GetValues(
                ParserWorkerAuthenticationOptions.SignatureHeaderName));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"text":"recognized total 42.00","pageCount":1,"meanConfidence":0.95,"isUsable":true}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}

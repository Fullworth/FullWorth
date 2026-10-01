using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class ParserWorkerPdfStatementTextExtractorTests
{
    private const string AuthenticationToken =
        "parser-worker-test-token-with-more-than-32-characters";

    [Fact]
    public void Extract_SendsPdfAndValidatesWorkerResponse()
    {
        var handler = new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/pdf/extract", request.RequestUri?.AbsolutePath);
            Assert.Equal("application/pdf", request.Content?.Headers.ContentType?.MediaType);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(AuthenticationToken, request.Headers.Authorization?.Parameter);

            var timestamp =
                request.Headers.GetValues(
                    ParserWorkerAuthenticationOptions.TimestampHeaderName)
                    .Single();
            var nonce =
                request.Headers.GetValues(
                    ParserWorkerAuthenticationOptions.NonceHeaderName)
                    .Single();
            var signature =
                request.Headers.GetValues(
                    ParserWorkerAuthenticationOptions.SignatureHeaderName)
                    .Single();

            Assert.InRange(
                long.Parse(timestamp, CultureInfo.InvariantCulture),
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 60);
            Assert.Matches("^[0-9a-f]{32}$", nonce);
            Assert.Matches("^[0-9a-f]{64}$", signature);

            var canonical =
                $"POST\n/v1/pdf/extract\n{timestamp}\n{nonce}";
            var expectedSignature =
                Convert.ToHexString(
                        HMACSHA256.HashData(
                            Encoding.UTF8.GetBytes(AuthenticationToken),
                            Encoding.UTF8.GetBytes(canonical)))
                    .ToLowerInvariant();
            Assert.Equal(expectedSignature, signature);
            Assert.Equal(new byte[] { 1, 2, 3 }, request.Content!.ReadAsByteArrayAsync().GetAwaiter().GetResult());
            return Json(HttpStatusCode.OK,
                """{"protocolVersion":1,"outcome":"text","errorCode":"","pageCount":2,"text":"Provider statement total due $94.99","requiresOcr":false}""");
        });
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://parser-worker:8081")
        };
        var extractor = CreateExtractor(client);

        var result = extractor.Extract(new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.Equal(2, result.PageCount);
        Assert.Equal("Provider statement total due $94.99", result.Text);
        Assert.False(result.RequiresOcr);
    }

    [Fact]
    public void Extract_RejectsWorkerOutputAboveCharacterLimit()
    {
        using var client = new HttpClient(new RecordingHandler(_ =>
            Json(HttpStatusCode.OK,
                $$"""{"protocolVersion":1,"outcome":"text","errorCode":"","pageCount":1,"text":"{{new string('x', 250_001)}}","requiresOcr":false}""")))
        {
            BaseAddress = new Uri("http://parser-worker:8081")
        };
        var extractor = CreateExtractor(client);

        var exception = Assert.Throws<BillStatementTextExtractionException>(
            () => extractor.Extract(new MemoryStream([1])));

        Assert.Contains("could not safely read", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Extract_RejectsOversizedInputBeforeCallingWorker()
    {
        var handler = new RecordingHandler(_ =>
            throw new InvalidOperationException("Worker must not be called."));
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://parser-worker:8081")
        };
        var extractor = CreateExtractor(client);

        using var input = new MemoryStream(new byte[(15 * 1024 * 1024) + 1]);

        var exception = Assert.Throws<BillStatementTextExtractionException>(
            () => extractor.Extract(input));

        Assert.Contains("15 MiB parser input limit", exception.Message, StringComparison.Ordinal);
        Assert.False(handler.Called);
    }

    [Fact]
    public void Extract_DoesNotExposeWorkerDiagnostics()
    {
        using var client = new HttpClient(new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("parser stack trace and document content")
            }))
        {
            BaseAddress = new Uri("http://parser-worker:8081")
        };
        var extractor = CreateExtractor(client);

        var exception = Assert.Throws<BillStatementTextExtractionException>(
            () => extractor.Extract(new MemoryStream([1])));

        Assert.DoesNotContain("stack trace", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("document content", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuthenticationOptions_RejectMissingProductionToken()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ParserWorkerAuthenticationOptions(
                configuredToken: null,
                isDevelopment: false));

        Assert.Contains("not configured securely", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ParserWorkerPdfStatementTextExtractor CreateExtractor(
        HttpClient client) =>
        new(
            client,
            new ParserWorkerAuthenticationOptions(
                AuthenticationToken,
                isDevelopment: false));

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public bool Called { get; private set; }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Called = true;
            return responseFactory(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Task.FromResult(responseFactory(request));
        }
    }
}

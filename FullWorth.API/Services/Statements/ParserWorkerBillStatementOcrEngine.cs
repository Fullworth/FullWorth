using System.Net;
using System.Security.Cryptography;
using System.Text.Json;

namespace FullWorth.API.Services.Statements;

/// <summary>
/// Sends the already-authorized statement bytes to the isolated parser worker.
/// Native PDF image decoding and Tesseract never execute in the API process.
/// </summary>
public sealed class ParserWorkerBillStatementOcrEngine : IBillStatementOcrEngine
{
    private const int MaxRequestBytes = 15 * 1024 * 1024;
    private const int MaxResponseBytes = 1_100_000;
    private const int MaxPages = 100;
    private const int MaxExtractedCharacters = 250_000;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ParserWorkerAuthenticationOptions _authentication;
    private readonly ILogger<ParserWorkerBillStatementOcrEngine> _logger;

    public ParserWorkerBillStatementOcrEngine(
        HttpClient httpClient,
        ParserWorkerAuthenticationOptions authentication,
        ILogger<ParserWorkerBillStatementOcrEngine> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public BillStatementOcrResult TryExtract(
        Stream source,
        string mediaType,
        string fileExtension)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead ||
            !IsSupported(mediaType, fileExtension))
        {
            return BillStatementOcrResult.Failure(0);
        }

        try
        {
            var body = ReadBounded(source);
            if (body is null)
            {
                return BillStatementOcrResult.Failure(0);
            }

            var contentSha256 =
                Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "/v1/ocr/extract")
            {
                Content = new ByteArrayContent(body)
            };
            request.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            request.Headers.Add("X-FullWorth-Ocr-Extension", fileExtension);
            _authentication.ApplyTo(request, contentSha256);

            using var response = _httpClient.Send(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken.None);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                return BillStatementOcrResult.Failure(0);
            }

            using var responseStream = response.Content.ReadAsStream();
            var responseBytes = ReadBoundedResponse(responseStream);
            if (responseBytes is null)
            {
                return BillStatementOcrResult.Failure(0);
            }

            var result = JsonSerializer.Deserialize<BillStatementOcrResult>(
                responseBytes,
                JsonOptions);
            if (result is null ||
                result.PageCount is < 0 or > MaxPages ||
                result.Text is null ||
                result.Text.Length > MaxExtractedCharacters ||
                result.MeanConfidence is < 0f or > 1f ||
                (result.IsUsable && string.IsNullOrWhiteSpace(result.Text)))
            {
                return BillStatementOcrResult.Failure(0);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Isolated statement OCR failed with {ExceptionType}.",
                ex.GetType().Name);
            return BillStatementOcrResult.Failure(0);
        }
    }

    private static byte[]? ReadBounded(Stream source)
    {
        if (source.CanSeek)
        {
            if (source.Length - source.Position > MaxRequestBytes)
            {
                return null;
            }
        }

        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (read > MaxRequestBytes - output.Length)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }
    }

    private static byte[]? ReadBoundedResponse(Stream source)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return output.ToArray();
            }

            if (read > MaxResponseBytes - output.Length)
            {
                return null;
            }

            output.Write(buffer, 0, read);
        }
    }

    private static bool IsSupported(string mediaType, string extension) =>
        string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
        (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase));
}

using System.Net;
using System.Text.Json;

namespace FullWorth.API.Services.Statements;

public interface IPdfStatementTextExtractor
{
    BillStatementTextExtractionResult Extract(Stream pdfStream);
}

public sealed class ParserWorkerPdfStatementTextExtractor : IPdfStatementTextExtractor
{
    private const int MaxPdfBytes = 15 * 1024 * 1024;
    private const int MaxResponseBytes = 1_100_000;
    private const int MaxPages = 100;
    private const int MaxExtractedCharacters = 250_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public ParserWorkerPdfStatementTextExtractor(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public BillStatementTextExtractionResult Extract(Stream pdfStream)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        if (!pdfStream.CanRead)
        {
            throw new ArgumentException("The PDF stream is not readable.", nameof(pdfStream));
        }

        if (pdfStream.CanSeek && pdfStream.Length - pdfStream.Position > MaxPdfBytes)
        {
            throw new BillStatementTextExtractionException(
                "The PDF exceeds the 15 MiB parser input limit.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/pdf/extract")
        {
            Content = new StreamContent(new BoundedReadStream(pdfStream, MaxPdfBytes))
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");

        try
        {
            using var response = _httpClient.Send(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                CancellationToken.None);

            if (response.StatusCode == HttpStatusCode.RequestEntityTooLarge)
            {
                throw new BillStatementTextExtractionException(
                    "The PDF exceeds the parser input limit.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BillStatementTextExtractionException(
                    "FullWorth could not safely read this PDF statement.");
            }

            if (response.Content.Headers.ContentLength is > MaxResponseBytes)
            {
                throw new BillStatementTextExtractionException(
                    "The PDF parser returned an oversized response.");
            }

            using var stream = response.Content.ReadAsStream();
            var responseBytes = ReadBoundedResponse(stream);
            var workerResponse = JsonSerializer.Deserialize<ParserWorkerResponse>(
                responseBytes,
                JsonOptions);

            if (workerResponse is null ||
                workerResponse.ProtocolVersion != 1 ||
                workerResponse.PageCount is < 0 or > MaxPages ||
                workerResponse.Text is null ||
                workerResponse.Text.Length > MaxExtractedCharacters ||
                workerResponse.Outcome is not ("text" or "needs_ocr"))
            {
                throw new BillStatementTextExtractionException(
                    "FullWorth could not safely read this PDF statement.");
            }

            return new BillStatementTextExtractionResult(
                workerResponse.Text,
                workerResponse.PageCount,
                workerResponse.RequiresOcr);
        }
        catch (BillStatementTextExtractionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new BillStatementTextExtractionException(
                "FullWorth could not safely read this PDF statement.",
                ex);
        }
    }

    private static byte[] ReadBoundedResponse(Stream source)
    {
        using var destination = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (read > MaxResponseBytes - destination.Length)
            {
                throw new BillStatementTextExtractionException(
                    "The PDF parser returned an oversized response.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private sealed class BoundedReadStream(Stream source, int maxBytes) : Stream
    {
        private long _read;

        public override bool CanRead => source.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var allowed = Math.Min(count, Math.Max(1, maxBytes - (int)_read + 1));
            var read = source.Read(buffer, offset, allowed);
            return CheckLimit(read);
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var allowed = Math.Min(buffer.Length, Math.Max(1, maxBytes - (int)_read + 1));
            var read = await source.ReadAsync(buffer[..allowed], cancellationToken);
            return CheckLimit(read);
        }

        private int CheckLimit(int read)
        {
            if (_read + read > maxBytes)
            {
                throw new BillStatementTextExtractionException("The PDF exceeds the 15 MiB parser input limit.");
            }
            _read += read;
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            // The caller owns the source stream.
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private sealed record ParserWorkerResponse(
        int ProtocolVersion,
        string Outcome,
        string ErrorCode,
        int PageCount,
        string Text,
        bool RequiresOcr);
}

using System.Text;
using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class PdfBillStatementAdversarialCorpusTests
{
    [Theory]
    [MemberData(nameof(TruncatedPdfCorpus))]
    public void Extract_RejectsTruncatedPdfCorpus(byte[] pdfBytes)
    {
        using var pdfStream = new MemoryStream(pdfBytes);
        var extractor = new PdfBillStatementTextExtractor();

        Assert.Throws<BillStatementTextExtractionException>(() => extractor.Extract(pdfStream));
    }

    public static IEnumerable<object[]> TruncatedPdfCorpus()
    {
        yield return new object[]
        {
            Encoding.ASCII.GetBytes(
                "%PDF-1.4\n" +
                "1 0 obj\n" +
                "<< /Type /Catalog /Pages 2 0 R >>\n" +
                "endobj\n" +
                "2 0 obj\n" +
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>\n" +
                "endobj\n" +
                "3 0 obj\n" +
                "<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>\n" +
                "endobj\n")
        };
    }

    [Fact]
    public void Extract_RejectsNonSeekablePdfAboveParserInputLimit()
    {
        using var pdfStream = new ChunkedReadOnlyStream(
            new byte[(15 * 1024 * 1024) + 1],
            chunkSize: 4093);
        var extractor = new PdfBillStatementTextExtractor();

        var exception = Assert.Throws<BillStatementTextExtractionException>(
            () => extractor.Extract(pdfStream));

        Assert.Contains("15 MiB parser input limit", exception.Message, StringComparison.Ordinal);
    }

    private sealed class ChunkedReadOnlyStream(byte[] bytes, int chunkSize) : Stream
    {
        private int _position;

        public override bool CanRead => true;
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
            ArgumentNullException.ThrowIfNull(buffer);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            if (offset > buffer.Length - count)
            {
                throw new ArgumentException("The requested buffer range is invalid.");
            }

            if (_position >= bytes.Length)
            {
                return 0;
            }

            var bytesToCopy = Math.Min(Math.Min(count, chunkSize), bytes.Length - _position);
            Buffer.BlockCopy(bytes, _position, buffer, offset, bytesToCopy);
            _position += bytesToCopy;
            return bytesToCopy;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

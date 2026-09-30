using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class PdfBillStatementNonSeekableInputTests
{
    [Fact]
    public void Extract_RejectsNonSeekablePdfAboveParserInputLimit()
    {
        using var source =
            new MemoryStream(
                new byte[(15 * 1024 * 1024) + 1]);

        using var pdfStream =
            new NonSeekableReadStream(source);

        var extractor =
            new PdfBillStatementTextExtractor();

        var exception =
            Assert.Throws<BillStatementTextExtractionException>(
                () => extractor.Extract(pdfStream));

        Assert.Contains(
            "15 MiB parser input limit",
            exception.Message,
            StringComparison.Ordinal);
    }

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly Stream _inner;

        public NonSeekableReadStream(Stream inner)
        {
            _inner = inner;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() =>
            throw new NotSupportedException();

        public override int Read(
            byte[] buffer,
            int offset,
            int count) =>
            _inner.Read(buffer, offset, count);

        public override long Seek(
            long offset,
            SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(
            byte[] buffer,
            int offset,
            int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

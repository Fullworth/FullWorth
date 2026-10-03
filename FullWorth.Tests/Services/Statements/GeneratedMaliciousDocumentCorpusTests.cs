using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services.Statements;

public sealed class GeneratedMaliciousDocumentCorpusTests
{
    [Theory]
    [MemberData(nameof(PdfCases))]
    public void PdfCorpus_FailsClosedWithSanitizedError(
        string name,
        byte[] bytes)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(
                name));

        using var stream =
            new MemoryStream(
                bytes,
                writable: false);

        var extractor =
            new PdfBillStatementTextExtractor();

        var exception =
            Assert.Throws<BillStatementTextExtractionException>(
                () =>
                    extractor.Extract(
                        stream));

        Assert.Equal(
            "FullWorth could not safely read this PDF statement.",
            exception.Message);

        Assert.DoesNotContain(
            name,
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ImageCases))]
    public void ImageCorpus_IsRejectedBeforeNativeDecode(
        string name,
        string mediaType,
        byte[] bytes)
    {
        Assert.False(
            string.IsNullOrWhiteSpace(
                name));

        Assert.False(
            EncodedOcrImageAdmission.TryAdmit(
                bytes,
                mediaType,
                out var pixelCount));

        Assert.Equal(
            0,
            pixelCount);
    }

    [Fact]
    public void Corpus_IsBoundedNamedAndDeterministic()
    {
        var first =
            GeneratedMaliciousDocumentCorpus.PdfCases
                .Concat(
                    GeneratedMaliciousDocumentCorpus.ImageCases)
                .ToArray();

        var second =
            GeneratedMaliciousDocumentCorpus.PdfCases
                .Concat(
                    GeneratedMaliciousDocumentCorpus.ImageCases)
                .ToArray();

        Assert.Equal(
            21,
            first.Length);

        Assert.Equal(
            first.Length,
            first
                .Select(
                    item =>
                        item.Name)
                .Distinct(
                    StringComparer.Ordinal)
                .Count());

        Assert.All(
            first,
            item =>
            {
                Assert.Matches(
                    "^[a-z0-9]+(?:-[a-z0-9]+)*$",
                    item.Name);

                Assert.InRange(
                    item.Bytes.Length,
                    1,
                    4 * 1024);

                Assert.Contains(
                    item.MediaType,
                    new[]
                    {
                        "application/pdf",
                        "image/png",
                        "image/jpeg"
                    });

                Assert.Contains(
                    item.FileExtension,
                    new[]
                    {
                        ".pdf",
                        ".png",
                        ".jpg"
                    });
            });

        Assert.Equal(
            first.Select(
                item =>
                    item.Name),
            second.Select(
                item =>
                    item.Name));

        for (var index = 0;
             index < first.Length;
             index++)
        {
            Assert.Equal(
                first[index].Bytes,
                second[index].Bytes);
        }
    }

    public static IEnumerable<object[]> PdfCases() =>
        GeneratedMaliciousDocumentCorpus.PdfCases
            .Select(
                item =>
                    new object[]
                    {
                        item.Name,
                        item.Bytes
                    });

    public static IEnumerable<object[]> ImageCases() =>
        GeneratedMaliciousDocumentCorpus.ImageCases
            .Select(
                item =>
                    new object[]
                    {
                        item.Name,
                        item.MediaType,
                        item.Bytes
                    });
}

using System.Globalization;
using System.Text;
using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class PdfBillStatementTextExtractorTests
{
    [Fact]
    public void Extract_ReadsUsefulEmbeddedText()
    {
        const string statementText =
            "Midco statement total amount due $104.99 for the May billing period.";

        using var pdfStream =
            new MemoryStream(
                CreatePdf(
                    statementText));

        var extractor =
            new PdfBillStatementTextExtractor();

        var result =
            extractor.Extract(
                pdfStream);

        Assert.Equal(
            1,
            result.PageCount);

        Assert.Contains(
            "Midco",
            result.Text,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "$104.99",
            result.Text,
            StringComparison.Ordinal);

        Assert.False(
            result.RequiresOcr);
    }

    [Fact]
    public void Extract_FlagsSparsePdfForOcr()
    {
        using var pdfStream =
            new MemoryStream(
                CreatePdf(
                    "$5"));

        var extractor =
            new PdfBillStatementTextExtractor();

        var result =
            extractor.Extract(
                pdfStream);

        Assert.Equal(
            1,
            result.PageCount);

        Assert.True(
            result.RequiresOcr);
    }

    [Fact]
    public void Extract_RejectsMalformedPdf()
    {
        using var pdfStream =
            new MemoryStream(
                Encoding.ASCII.GetBytes(
                    "%PDF-1.7\nThis is not a valid PDF document."));

        var extractor =
            new PdfBillStatementTextExtractor();

        Assert.Throws<BillStatementTextExtractionException>(
            () =>
                extractor.Extract(
                    pdfStream));
    }

    [Fact]
    public void Extract_RejectsPdfAbovePageLimit()
    {
        using var pdfStream =
            new MemoryStream(
                CreatePdf(
                    "Statement text",
                    pageCount: 101));

        var extractor =
            new PdfBillStatementTextExtractor();

        var exception =
            Assert.Throws<BillStatementTextExtractionException>(
                () =>
                    extractor.Extract(
                        pdfStream));

        Assert.Contains(
            "100-page processing limit",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_LimitsCharactersFromLargeTextPage()
    {
        using var pdfStream =
            new MemoryStream(
                CreatePdf(
                    new string(
                        'A',
                        260_000)));

        var extractor =
            new PdfBillStatementTextExtractor();

        var result =
            extractor.Extract(
                pdfStream);

        Assert.InRange(
            result.Text.Length,
            40,
            250_000);

        Assert.False(
            result.RequiresOcr);
    }

    private static byte[] CreatePdf(
        string text,
        int pageCount = 1)
    {
        if (pageCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageCount));
        }

        var escapedText =
            text
                .Replace(
                    "\\",
                    "\\\\",
                    StringComparison.Ordinal)
                .Replace(
                    "(",
                    "\\(",
                    StringComparison.Ordinal)
                .Replace(
                    ")",
                    "\\)",
                    StringComparison.Ordinal);

        var contentStream =
            $"BT\n/F1 12 Tf\n72 720 Td\n({escapedText}) Tj\nET\n";

        var fontObjectNumber =
            3 + pageCount;

        var firstContentObjectNumber =
            fontObjectNumber + 1;

        var pageReferences =
            string.Join(
                " ",
                Enumerable
                    .Range(
                        3,
                        pageCount)
                    .Select(
                        objectNumber =>
                            $"{objectNumber} 0 R"));

        var objects =
            new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",

                $"<< /Type /Pages /Kids [{pageReferences}] /Count {pageCount} >>"
            };

        for (var pageIndex = 0;
             pageIndex < pageCount;
             pageIndex++)
        {
            var contentObjectNumber =
                firstContentObjectNumber +
                pageIndex;

            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {fontObjectNumber} 0 R >> >> /Contents {contentObjectNumber} 0 R >>");
        }

        objects.Add(
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        for (var pageIndex = 0;
             pageIndex < pageCount;
             pageIndex++)
        {
            objects.Add(
                $"<< /Length {Encoding.ASCII.GetByteCount(contentStream)} >>\nstream\n{contentStream}endstream");
        }

        using var output =
            new MemoryStream();

        WriteAscii(
            output,
            "%PDF-1.4\n");

        var offsets =
            new List<long>
            {
                0
            };

        for (var index = 0;
             index < objects.Count;
             index++)
        {
            offsets.Add(
                output.Position);

            WriteAscii(
                output,
                $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var crossReferenceOffset =
            output.Position;

        WriteAscii(
            output,
            $"xref\n0 {objects.Count + 1}\n");

        WriteAscii(
            output,
            "0000000000 65535 f \n");

        for (var index = 1;
             index < offsets.Count;
             index++)
        {
            WriteAscii(
                output,
                $"{offsets[index].ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{crossReferenceOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");

        return output.ToArray();
    }

    private static void WriteAscii(
        Stream destination,
        string value)
    {
        var bytes =
            Encoding.ASCII.GetBytes(
                value);

        destination.Write(
            bytes,
            0,
            bytes.Length);
    }
}
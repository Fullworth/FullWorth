using FullWorth.Tests.Services;

namespace FullWorth.Tests.Services;

public sealed class PdfStatementTextParserTests
{
    [Fact]
    public void Extract_ReadsUsefulEmbeddedPdfText()
    {
        using var input = new MemoryStream(
            PdfBillStatementTextExtractorTests.CreatePdf(
                "Midco statement total due $104.99 for the May billing period."));

        var response = new PdfStatementTextParser().Extract(input);

        Assert.Equal("text", response.Outcome);
        Assert.Equal(1, response.PageCount);
        Assert.Contains("Midco", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("$104.99", response.Text, StringComparison.Ordinal);
        Assert.False(response.RequiresOcr);
    }

    [Fact]
    public void Extract_FlagsSparsePdfForOcr()
    {
        using var input = new MemoryStream(
            PdfBillStatementTextExtractorTests.CreatePdf("$5"));

        var response = new PdfStatementTextParser().Extract(input);

        Assert.Equal("needs_ocr", response.Outcome);
        Assert.Equal(1, response.PageCount);
        Assert.True(response.RequiresOcr);
    }

    [Fact]
    public void Extract_RejectsPdfAbovePageLimitWithStableCode()
    {
        using var input = new MemoryStream(
            PdfBillStatementTextExtractorTests.CreatePdf("Statement", pageCount: 101));

        var response = new PdfStatementTextParser().Extract(input);

        Assert.Equal("rejected", response.Outcome);
        Assert.Equal("page_limit_exceeded", response.ErrorCode);
        Assert.Equal(101, response.PageCount);
        Assert.Empty(response.Text);
    }

    [Fact]
    public void Extract_CapsTextFromLargeTextPage()
    {
        using var input = new MemoryStream(
            PdfBillStatementTextExtractorTests.CreatePdf(new string('A', 260_000)));

        var response = new PdfStatementTextParser().Extract(input);

        Assert.Equal("text", response.Outcome);
        Assert.InRange(response.Text.Length, 40, PdfStatementTextParser.MaxExtractedCharacters);
        Assert.False(response.RequiresOcr);
    }

    [Fact]
    public void Extract_RejectsMalformedPdfWithoutParserDiagnostics()
    {
        using var input = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(
            "%PDF-1.7\nThis is not a valid PDF document."));

        var response = new PdfStatementTextParser().Extract(input);

        Assert.Equal("rejected", response.Outcome);
        Assert.Equal("document_rejected", response.ErrorCode);
        Assert.Empty(response.Text);
    }
}

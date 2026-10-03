using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace FullWorth.Tests.Services;

public sealed class ParserWorkerAdversarialPdfCorpusTests
{
    [Theory]
    [MemberData(nameof(MalformedCorpus))]
    public void Extract_RejectsMalformedCorpusWithoutDiagnostics(
        string caseName,
        byte[] input,
        string expectedCode)
    {
        _ = caseName;
        using var stream = new MemoryStream(input);

        var response = new PdfStatementTextParser().Extract(stream);

        Assert.Equal("rejected", response.Outcome);
        Assert.Equal(expectedCode, response.ErrorCode);
        Assert.Empty(response.Text);
        Assert.False(response.RequiresOcr);
    }

    public static IEnumerable<object[]> MalformedCorpus()
    {
        yield return Case(
            "non-pdf payload",
            Encoding.ASCII.GetBytes("not a PDF document"),
            "invalid_pdf_signature");

        yield return Case(
            "truncated object graph",
            Encoding.ASCII.GetBytes(
                "%PDF-1.7\n" +
                "1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n" +
                "2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n" +
                "3 0 obj\n<< /Type /Page /Parent 2 0 R /Contents 4 0 R >>\nendobj\n"),
            "document_rejected");

        yield return Case(
            "dangling trailer root",
            Encoding.ASCII.GetBytes(
                "%PDF-1.7\n" +
                "xref\n0 2\n0000000000 65535 f \n0000000000 00000 n \n" +
                "trailer\n<< /Size 2 /Root 99 0 R >>\nstartxref\n9\n%%EOF"),
            "document_rejected");

        yield return Case(
            "oversized declared stream",
            CreateFilteredPdf(
                Encoding.ASCII.GetBytes("not-compressed"),
                "/FlateDecode",
                declaredLength: PdfStatementTextParser.MaxInputBytes + 1),
            "document_rejected");

        yield return Case(
            "invalid ascii hex filter data",
            CreateFilteredPdf(
                Encoding.ASCII.GetBytes("GG>"),
                "/ASCIIHexDecode"),
            "document_rejected");

        yield return Case(
            "invalid flate stream",
            CreateFilteredPdf(
                Encoding.ASCII.GetBytes("nope"),
                "/FlateDecode"),
            "document_rejected");
    }

    [Fact]
    public void Extract_BoundsTextFromHighCompressionExpansionFixture()
    {
        var pdf = CreateCompressedPdf(new string('A', 300_000));
        Assert.InRange(pdf.Length, 1, 10_000);

        using var stream = new MemoryStream(pdf);
        var response = new PdfStatementTextParser().Extract(stream);

        Assert.Equal("text", response.Outcome);
        Assert.Equal(1, response.PageCount);
        Assert.Equal(PdfStatementTextParser.MaxExtractedCharacters, response.Text.Length);
        Assert.False(response.RequiresOcr);
    }

    private static object[] Case(
        string name,
        byte[] input,
        string expectedCode) =>
        [name, input, expectedCode];

    private static byte[] CreateCompressedPdf(string text)
    {
        var content = Encoding.ASCII.GetBytes(
            $"BT\n/F1 12 Tf\n72 720 Td\n({text}) Tj\nET\n");
        byte[] compressed;
        using (var compressedOutput = new MemoryStream())
        {
            using (var compressor = new ZLibStream(
                       compressedOutput,
                       CompressionLevel.SmallestSize,
                       leaveOpen: true))
            {
                compressor.Write(content);
            }

            compressed = compressedOutput.ToArray();
        }

        return CreateFilteredPdf(compressed, "/FlateDecode");
    }

    private static byte[] CreateFilteredPdf(
        byte[] streamBytes,
        string filter,
        int? declaredLength = null)
    {
        using var output = new MemoryStream();
        WriteAscii(output, "%PDF-1.7\n");

        var offsets = new List<long> { 0 };
        WriteObject(output, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
        WriteObject(output, offsets, 2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>");
        WriteObject(
            output,
            offsets,
            3,
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
            "/Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>");
        WriteObject(output, offsets, 4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");

        offsets.Add(output.Position);
        WriteAscii(
            output,
            $"5 0 obj\n<< /Length {(declaredLength ?? streamBytes.Length).ToString(CultureInfo.InvariantCulture)} /Filter {filter} >>\nstream\n");
        output.Write(streamBytes);
        WriteAscii(output, "\nendstream\nendobj\n");

        var xrefOffset = output.Position;
        WriteAscii(output, "xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
        {
            WriteAscii(
                output,
                $"{offset.ToString("D10", CultureInfo.InvariantCulture)} 00000 n \n");
        }

        WriteAscii(
            output,
            $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset.ToString(CultureInfo.InvariantCulture)}\n%%EOF\n");
        return output.ToArray();
    }

    private static void WriteObject(
        Stream output,
        ICollection<long> offsets,
        int objectNumber,
        string body)
    {
        offsets.Add(output.Position);
        WriteAscii(output, $"{objectNumber} 0 obj\n{body}\nendobj\n");
    }

    private static void WriteAscii(Stream output, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        output.Write(bytes);
    }
}

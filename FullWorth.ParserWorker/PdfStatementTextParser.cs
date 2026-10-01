using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

public sealed class PdfStatementTextParser
{
    public const int MaxInputBytes = 15 * 1024 * 1024;
    public const int MaxPages = 100;
    public const int MaxExtractedCharacters = 250_000;
    private const int MinimumUsefulCharacters = 40;

    public WorkerProtocol.WorkerResponse Extract(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead)
        {
            return Rejected("input_not_readable");
        }

        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            while (true)
            {
                var read = input.Read(chunk, 0, chunk.Length);
                if (read == 0)
                {
                    break;
                }

                if (read > MaxInputBytes - buffer.Length)
                {
                    return Rejected("input_too_large");
                }

                buffer.Write(chunk, 0, read);
            }

            buffer.Position = 0;
            using var document = PdfDocument.Open(buffer);
            var text = new StringBuilder();
            var pageCount = 0;

            foreach (var page in document.GetPages())
            {
                pageCount++;
                if (pageCount > MaxPages)
                {
                    return new WorkerProtocol.WorkerResponse(
                        1, "rejected", "page_limit_exceeded", pageCount);
                }

                var pageText = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(pageText))
                {
                    continue;
                }

                if (text.Length > 0)
                {
                    text.AppendLine();
                    text.AppendLine();
                }

                var remaining = MaxExtractedCharacters - text.Length;
                if (remaining <= 0)
                {
                    break;
                }

                text.Append(pageText.AsSpan(0, Math.Min(pageText.Length, remaining)));
                if (pageText.Length > remaining)
                {
                    break;
                }
            }

            var normalized = Normalize(text.ToString());
            var needsOcr = normalized.Length < MinimumUsefulCharacters;
            return new WorkerProtocol.WorkerResponse(
                1,
                needsOcr ? "needs_ocr" : "text",
                string.Empty,
                pageCount,
                normalized,
                needsOcr);
        }
        catch
        {
            // Parser diagnostics and document content never cross the worker boundary.
            return Rejected("document_rejected");
        }
    }

    private static WorkerProtocol.WorkerResponse Rejected(string code) =>
        new(1, "rejected", code);

    private static string Normalize(string value)
    {
        var output = new StringBuilder(Math.Min(value.Length, MaxExtractedCharacters));
        var hasContent = false;
        var pendingBlankLine = false;

        foreach (var rawLine in value.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                if (hasContent)
                {
                    pendingBlankLine = true;
                }
                continue;
            }

            if (hasContent)
            {
                output.AppendLine();
                if (pendingBlankLine)
                {
                    output.AppendLine();
                }
            }

            var remaining = MaxExtractedCharacters - output.Length;
            if (remaining <= 0)
            {
                break;
            }
            output.Append(line.AsSpan(0, Math.Min(line.Length, remaining)));
            hasContent = true;
            pendingBlankLine = false;
        }

        return output.ToString();
    }
}

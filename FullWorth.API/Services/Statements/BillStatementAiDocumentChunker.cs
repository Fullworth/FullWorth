namespace FullWorth.API.Services.Statements;

/// <summary>
/// Deterministically partitions statement text into bounded, contiguous chunks
/// without dropping, normalizing, duplicating, or reordering source text.
///
/// This class only plans source windows. It does not run AI, merge extracted
/// facts, reconcile money, or decide which chunk owns a fact.
/// </summary>
public sealed class BillStatementAiDocumentChunker
{
    public IReadOnlyList<BillStatementAiDocumentChunk> Plan(
        string documentText,
        int maxCharacters)
    {
        ArgumentNullException.ThrowIfNull(
            documentText);

        if (maxCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharacters),
                maxCharacters,
                "Maximum chunk size must be positive.");
        }

        if (documentText.Length == 0)
        {
            return [];
        }

        var chunks =
            new List<BillStatementAiDocumentChunk>();

        var startOffset =
            0;

        while (startOffset < documentText.Length)
        {
            var remaining =
                documentText.Length -
                startOffset;

            var tentativeLength =
                Math.Min(
                    maxCharacters,
                    remaining);

            var endOffset =
                startOffset +
                tentativeLength;

            if (endOffset < documentText.Length)
            {
                endOffset =
                    FindPreferredEndOffset(
                        documentText,
                        startOffset,
                        endOffset);
            }

            if (endOffset <= startOffset)
            {
                endOffset =
                    Math.Min(
                        startOffset +
                        maxCharacters,
                        documentText.Length);
            }

            var length =
                endOffset -
                startOffset;

            chunks.Add(
                new BillStatementAiDocumentChunk(
                    Index:
                        chunks.Count,

                    StartOffset:
                        startOffset,

                    Text:
                        documentText.Substring(
                            startOffset,
                            length)));

            startOffset =
                endOffset;
        }

        return chunks;
    }

    private static int FindPreferredEndOffset(
        string documentText,
        int startOffset,
        int tentativeEndOffset)
    {
        /*
         * Prefer complete source lines because statement extraction is commonly
         * line-oriented. Search backward for LF and include it in the chunk so
         * reconstruction remains exact.
         */
        for (var index =
                 tentativeEndOffset - 1;
             index >= startOffset;
             index--)
        {
            if (documentText[index] == '\n')
            {
                return index + 1;
            }
        }

        /*
         * OCR/PDF extraction can produce very long logical lines. When there
         * is no newline inside the bounded window, prefer a nearby whitespace
         * boundary instead of cutting through a token, amount, provider name,
         * or line-item description. Restrict the search to the final quarter
         * of the window so a single early space cannot create a tiny chunk and
         * multiply model calls.
         */
        var minimumSoftBoundary =
            startOffset +
            Math.Max(
                1,
                (
                    tentativeEndOffset -
                    startOffset) *
                3 /
                4);

        for (var index =
                 tentativeEndOffset - 1;
             index >= minimumSoftBoundary;
             index--)
        {
            if (char.IsWhiteSpace(
                    documentText[index]) &&
                documentText[index] !=
                    '\r' &&
                documentText[index] !=
                    '\n')
            {
                return index + 1;
            }
        }

        /*
         * If no safe soft boundary exists, split the long line at the hard
         * bound. Avoid splitting a CRLF pair when the bound lands between
         * '\r' and '\n'.
         */
        if (tentativeEndOffset >
                startOffset &&
            documentText[
                tentativeEndOffset - 1] ==
                '\r' &&
            documentText[
                tentativeEndOffset] ==
                '\n')
        {
            var beforeCarriageReturn =
                tentativeEndOffset - 1;

            if (beforeCarriageReturn >
                startOffset)
            {
                return beforeCarriageReturn;
            }
        }

        return tentativeEndOffset;
    }
}

public sealed record BillStatementAiDocumentChunk(
    int Index,
    int StartOffset,
    string Text)
{
    public int Length =>
        Text.Length;

    public int EndOffset =>
        checked(
            StartOffset +
            Length);
}

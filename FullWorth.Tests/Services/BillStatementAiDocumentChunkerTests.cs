using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiDocumentChunkerTests
{
    [Fact]
    public void Plan_ShortDocument_ReturnsSingleExactChunk()
    {
        const string text =
            "Provider: ACME\r\nTotal due: $42.10\r\n";

        var chunks =
            new BillStatementAiDocumentChunker()
                .Plan(
                    text,
                    maxCharacters:
                        1_000);

        var chunk =
            Assert.Single(
                chunks);

        Assert.Equal(
            0,
            chunk.Index);

        Assert.Equal(
            0,
            chunk.StartOffset);

        Assert.Equal(
            text.Length,
            chunk.EndOffset);

        Assert.Equal(
            text,
            chunk.Text);
    }

    [Fact]
    public void Plan_OversizedDocument_RoundTripsWithoutLossOrDuplication()
    {
        string text =
            string.Join(
                "\n",
                Enumerable.Range(
                        1,
                        80)
                    .Select(
                        number =>
                            $"line-{number:D3}: {new string('x', number % 19)}")) +
            "\n";

        const int maxCharacters =
            97;

        var chunks =
            new BillStatementAiDocumentChunker()
                .Plan(
                    text,
                    maxCharacters);

        Assert.True(
            chunks.Count > 1);

        Assert.All(
            chunks,
            chunk =>
                Assert.InRange(
                    chunk.Length,
                    1,
                    maxCharacters));

        Assert.Equal(
            text,
            string.Concat(
                chunks.Select(
                    chunk =>
                        chunk.Text)));

        for (var index = 0;
             index < chunks.Count;
             index++)
        {
            var chunk =
                chunks[index];

            Assert.Equal(
                index,
                chunk.Index);

            Assert.Equal(
                index == 0
                    ? 0
                    : chunks[index - 1].EndOffset,
                chunk.StartOffset);
        }

        Assert.Equal(
            text.Length,
            chunks[^1].EndOffset);
    }

    [Fact]
    public void Plan_PrefersCompleteLineBoundaryInsideLimit()
    {
        const string text =
            "first line\nsecond line is longer\nthird\n";

        var chunks =
            new BillStatementAiDocumentChunker()
                .Plan(
                    text,
                    maxCharacters:
                        20);

        Assert.Equal(
            "first line\n",
            chunks[0].Text);

        Assert.Equal(
            "second line is longe",
            chunks[1].Text);

        Assert.Equal(
            text,
            string.Concat(
                chunks.Select(
                    chunk =>
                        chunk.Text)));
    }

    [Fact]
    public void Plan_LongLine_UsesHardBoundWithoutDroppingCharacters()
    {
        string text =
            new(
                'a',
                205);

        const int maxCharacters =
            64;

        var chunks =
            new BillStatementAiDocumentChunker()
                .Plan(
                    text,
                    maxCharacters);

        Assert.Equal(
            [64, 64, 64, 13],
            chunks.Select(
                    chunk =>
                        chunk.Length)
                .ToArray());

        Assert.Equal(
            text,
            string.Concat(
                chunks.Select(
                    chunk =>
                        chunk.Text)));
    }

    [Fact]
    public void Plan_DoesNotSplitCrLfPairAtHardBoundary()
    {
        const string text =
            "123456789\r\nabcdefghij";

        var chunks =
            new BillStatementAiDocumentChunker()
                .Plan(
                    text,
                    maxCharacters:
                        10);

        Assert.DoesNotContain(
            chunks,
            chunk =>
                chunk.Text.EndsWith(
                    "\r",
                    StringComparison.Ordinal));

        Assert.Equal(
            text,
            string.Concat(
                chunks.Select(
                    chunk =>
                        chunk.Text)));
    }

    [Fact]
    public void Plan_EmptyDocument_ReturnsNoChunks()
    {
        var chunks =
            new BillStatementAiDocumentChunker()
                .Plan(
                    string.Empty,
                    maxCharacters:
                        100);

        Assert.Empty(
            chunks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Plan_NonPositiveMaximum_Throws(
        int maxCharacters)
    {
        Assert.Throws<
            ArgumentOutOfRangeException>(
            () =>
                new BillStatementAiDocumentChunker()
                    .Plan(
                        "statement",
                        maxCharacters));
    }
}

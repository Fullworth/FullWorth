using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthByteTokenizerTests
{
    private readonly FullWorthByteTokenizer _tokenizer = new();

    [Fact]
    public void Vocabulary_HasStableVersionAndByteMapping()
    {
        Assert.Equal("fullworth-utf8-byte-v1", FullWorthByteTokenizer.Version);
        Assert.Equal(259, FullWorthByteTokenizer.VocabularySize);
        Assert.Equal(0, FullWorthByteTokenizer.PaddingToken);
        Assert.Equal(new[] { 1, 68, 198, 172, 2 }, _tokenizer.Encode("Aé"));
        Assert.Equal(new[] { 1, 2 }, _tokenizer.Encode(string.Empty));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Total $104.99\r\nDiscount -$20.00")]
    [InlineData("Électricité €42.50 — 水道 😀")]
    [InlineData("e\u0301\t\0\uFEFF")]
    [InlineData("<end><begin>[PAD]")]
    public void RoundTrip_PreservesExactEvidenceText(string text)
    {
        Assert.Equal(text, _tokenizer.Decode(_tokenizer.Encode(text)));
    }

    [Fact]
    public void Encode_EnforcesUtf8ByteLimitWithoutTruncation()
    {
        string atLimit = new('é', FullWorthByteTokenizer.MaxUtf8Bytes / 2);
        int[] tokens = _tokenizer.Encode(atLimit);
        Assert.Equal(FullWorthByteTokenizer.MaxUtf8Bytes + 2, tokens.Length);
        Assert.Equal(atLimit, _tokenizer.Decode(tokens));
        Assert.Throws<ArgumentException>(() => _tokenizer.Encode(atLimit + "a"));
        Assert.Throws<ArgumentException>(() =>
            _tokenizer.Encode(new string('a', FullWorthByteTokenizer.MaxUtf8Bytes + 1)));
    }

    [Fact]
    public void Encode_RejectsInvalidUnicodeWithoutEchoingInput()
    {
        const string sensitiveText = "private-fixture-value";
        foreach (string malformed in new[] { "\uD800", "\uDC00" })
        {
            var error = Assert.Throws<ArgumentException>(() =>
                _tokenizer.Encode(sensitiveText + malformed));
            Assert.DoesNotContain(sensitiveText, error.ToString());
            Assert.Null(error.InnerException);
        }

        Assert.Throws<ArgumentNullException>(() => _tokenizer.Encode(null!));
    }

    [Fact]
    public void Decode_RejectsBadFramingReservedTokensAndInvalidByteIds()
    {
        int[][] invalid =
        [
            [], [1], [2, 1], [1, 68], [68, 2],
            [1, 0, 2], [1, 1, 2], [1, 2, 2],
            [1, -1, 2], [1, 259, 2], [1, int.MaxValue, 2],
            [1, int.MinValue, 2]
        ];

        foreach (int[] tokens in invalid)
            Assert.Throws<ArgumentException>(() => _tokenizer.Decode(tokens));

        Assert.Throws<ArgumentException>(() =>
            _tokenizer.Decode(new int[FullWorthByteTokenizer.MaxUtf8Bytes + 3]));
    }

    [Fact]
    public void Decode_RejectsMalformedUtf8WithoutReplacementCharacters()
    {
        // Truncated sequence, overlong encoding, UTF-8 surrogate, > U+10FFFF.
        int[][] invalid =
        [
            [1, 0xC3 + 3, 2],
            [1, 0xC0 + 3, 0xAF + 3, 2],
            [1, 0xED + 3, 0xA0 + 3, 0x80 + 3, 2],
            [1, 0xF4 + 3, 0x90 + 3, 0x80 + 3, 0x80 + 3, 2]
        ];

        foreach (int[] tokens in invalid)
        {
            var error = Assert.Throws<ArgumentException>(() => _tokenizer.Decode(tokens));
            Assert.Null(error.InnerException);
        }
    }
}

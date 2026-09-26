using System.Text;

namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// First-party byte vocabulary for from-scratch model experiments.
/// Tokenization neither authorizes data use nor validates financial evidence.
/// </summary>
public sealed class FullWorthByteTokenizer
{
    // Persist this version with model checkpoints; token IDs must stay stable.
    public const string Version = "fullworth-utf8-byte-v1";
    public const int PaddingToken = 0;
    public const int BeginToken = 1;
    public const int EndToken = 2;
    public const int ByteOffset = 3;
    public const int VocabularySize = ByteOffset + 256;
    public const int MaxUtf8Bytes = 16_384;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>
    /// Encodes one bounded sequence, including begin/end tokens, without
    /// normalizing, truncating, or silently replacing source characters.
    /// Callers must select authorized, minimized evidence before invoking this.
    /// </summary>
    public int[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Bound work before scanning UTF-16; valid UTF-8 needs at least as
        // many bytes as UTF-16 code units, including surrogate pairs.
        if (text.Length > MaxUtf8Bytes)
            throw InvalidText();

        try
        {
            int byteCount = StrictUtf8.GetByteCount(text);
            if (byteCount > MaxUtf8Bytes)
                throw InvalidText();

            byte[] bytes = StrictUtf8.GetBytes(text);
            int[] tokens = new int[byteCount + 2];
            tokens[0] = BeginToken;
            tokens[^1] = EndToken;

            for (int index = 0; index < bytes.Length; index++)
                tokens[index + 1] = bytes[index] + ByteOffset;

            return tokens;
        }
        catch (EncoderFallbackException)
        {
            // Never include sensitive source text or encoder details in errors.
            throw InvalidText();
        }
    }

    /// <summary>
    /// Decodes one complete unpadded sequence. Partial generated UTF-8,
    /// embedded control tokens, and malformed framing are rejected.
    /// Decoded text is untrusted model/data output, not validated evidence.
    /// </summary>
    public string Decode(ReadOnlySpan<int> tokens)
    {
        if (tokens.Length < 2 || tokens.Length > MaxUtf8Bytes + 2 ||
            tokens[0] != BeginToken || tokens[^1] != EndToken)
            throw InvalidTokens();

        byte[] bytes = new byte[tokens.Length - 2];
        for (int index = 0; index < bytes.Length; index++)
        {
            int token = tokens[index + 1];
            if (token < ByteOffset || token >= VocabularySize)
                throw InvalidTokens();

            bytes[index] = (byte)(token - ByteOffset);
        }

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw InvalidTokens();
        }
    }

    private static ArgumentException InvalidText() =>
        new("Text must be valid Unicode within the UTF-8 byte limit.", "text");

    private static ArgumentException InvalidTokens() =>
        new("Tokens must form a bounded, complete UTF-8 sequence.", "tokens");
}

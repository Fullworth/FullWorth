namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// One fixed-width causal language-model training example.
/// Padding positions are excluded from attention and loss.
/// </summary>
public sealed record FullWorthNextTokenTrainingExample(
    int[] InputTokens,
    int[] TargetTokens,
    bool[] AttentionMask,
    bool[] LossMask);

/// <summary>
/// Converts one authorized text document into bounded next-token examples.
/// Documents are never concatenated, so training examples cannot cross a
/// document boundary accidentally.
/// </summary>
public sealed class FullWorthNextTokenTrainingSetBuilder
{
    private readonly FullWorthByteTokenizer _tokenizer;
    private readonly FullWorthLanguageModelArchitecture _architecture;

    public FullWorthNextTokenTrainingSetBuilder(
        FullWorthByteTokenizer tokenizer,
        FullWorthLanguageModelArchitecture architecture)
    {
        _tokenizer =
            tokenizer ??
            throw new ArgumentNullException(nameof(tokenizer));

        _architecture =
            architecture ??
            throw new ArgumentNullException(nameof(architecture));
    }

    public IReadOnlyList<FullWorthNextTokenTrainingExample> Build(
        string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        int[] tokens = _tokenizer.Encode(text);
        int predictionCount = tokens.Length - 1;
        int contextLength = _architecture.ContextLength;

        var examples =
            new List<FullWorthNextTokenTrainingExample>(
                (predictionCount + contextLength - 1) / contextLength);

        for (int offset = 0;
             offset < predictionCount;
             offset += contextLength)
        {
            int populated =
                Math.Min(
                    contextLength,
                    predictionCount - offset);

            int[] inputTokens =
                new int[contextLength];

            int[] targetTokens =
                new int[contextLength];

            bool[] attentionMask =
                new bool[contextLength];

            bool[] lossMask =
                new bool[contextLength];

            Array.Fill(
                inputTokens,
                FullWorthByteTokenizer.PaddingToken);

            Array.Fill(
                targetTokens,
                FullWorthByteTokenizer.PaddingToken);

            for (int index = 0;
                 index < populated;
                 index++)
            {
                inputTokens[index] =
                    tokens[offset + index];

                targetTokens[index] =
                    tokens[offset + index + 1];

                attentionMask[index] = true;
                lossMask[index] = true;
            }

            examples.Add(
                new FullWorthNextTokenTrainingExample(
                    inputTokens,
                    targetTokens,
                    attentionMask,
                    lossMask));
        }

        return examples;
    }
}

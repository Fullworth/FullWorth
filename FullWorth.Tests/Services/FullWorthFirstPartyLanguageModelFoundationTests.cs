using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthFirstPartyLanguageModelFoundationTests
{
    [Fact]
    public void BootstrapArchitecture_IsVersionedModernAndNontrivial()
    {
        var architecture =
            FullWorthLanguageModelArchitecture.CreateBootstrap();

        Assert.Equal(
            "fullworth-decoder-v1",
            architecture.Version);

        Assert.Equal(
            FullWorthByteTokenizer.Version,
            architecture.TokenizerVersion);

        Assert.Equal(
            FullWorthByteTokenizer.VocabularySize,
            architecture.VocabularySize);

        Assert.Equal(1_024, architecture.ContextLength);
        Assert.Equal(512, architecture.HiddenSize);
        Assert.Equal(8, architecture.LayerCount);
        Assert.Equal(8, architecture.AttentionHeadCount);
        Assert.Equal(4, architecture.KeyValueHeadCount);
        Assert.Equal(64, architecture.HeadSize);
        Assert.Equal(1_536, architecture.FeedForwardSize);

        Assert.True(architecture.UsesCausalAttention);
        Assert.True(architecture.UsesRotaryPositionEncoding);
        Assert.True(architecture.UsesRmsNormalization);
        Assert.True(architecture.UsesSwiGlu);
        Assert.True(architecture.TiesOutputEmbedding);

        Assert.InRange(
            architecture.EstimateParameterCount(),
            20_000_000,
            40_000_000);

        Assert.Equal(64, architecture.CompatibilityId.Length);
        Assert.All(
            architecture.CompatibilityId,
            character =>
                Assert.True(
                    char.IsAsciiHexDigit(character) &&
                    !char.IsUpper(character)));
    }

    [Fact]
    public void CompatibilityId_ChangesWhenTensorShapeChanges()
    {
        var first = CreateArchitecture(contextLength: 8);
        var second = CreateArchitecture(contextLength: 9);

        Assert.NotEqual(
            first.CompatibilityId,
            second.CompatibilityId);
    }

    [Fact]
    public void Architecture_RejectsIncompatibleOrInvalidShapes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize + 1,
                contextLength: 8,
                hiddenSize: 64,
                layerCount: 2,
                attentionHeadCount: 4,
                keyValueHeadCount: 2,
                feedForwardSize: 128));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength: 8,
                hiddenSize: 65,
                layerCount: 2,
                attentionHeadCount: 4,
                keyValueHeadCount: 2,
                feedForwardSize: 130));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength: 8,
                hiddenSize: 64,
                layerCount: 2,
                attentionHeadCount: 4,
                keyValueHeadCount: 3,
                feedForwardSize: 128));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength: 8,
                hiddenSize: 64,
                layerCount: 2,
                attentionHeadCount: 4,
                keyValueHeadCount: 2,
                feedForwardSize: 128,
                rotaryTheta: double.NaN));
    }

    [Fact]
    public void TrainingBuilder_CreatesExactShiftedTargetsAndPadding()
    {
        var tokenizer =
            new FullWorthByteTokenizer();

        var builder =
            new FullWorthNextTokenTrainingSetBuilder(
                tokenizer,
                CreateArchitecture(contextLength: 4));

        FullWorthNextTokenTrainingExample example =
            Assert.Single(builder.Build("AB"));

        Assert.Equal(
            new[] { 1, 68, 69, 0 },
            example.InputTokens);

        Assert.Equal(
            new[] { 68, 69, 2, 0 },
            example.TargetTokens);

        Assert.Equal(
            new[] { true, true, true, false },
            example.AttentionMask);

        Assert.Equal(
            new[] { true, true, true, false },
            example.LossMask);
    }

    [Fact]
    public void TrainingBuilder_EmptyDocumentStillLearnsSequenceBoundary()
    {
        var builder =
            new FullWorthNextTokenTrainingSetBuilder(
                new FullWorthByteTokenizer(),
                CreateArchitecture(contextLength: 4));

        FullWorthNextTokenTrainingExample example =
            Assert.Single(builder.Build(string.Empty));

        Assert.Equal(
            new[] { 1, 0, 0, 0 },
            example.InputTokens);

        Assert.Equal(
            new[] { 2, 0, 0, 0 },
            example.TargetTokens);

        Assert.Equal(
            new[] { true, false, false, false },
            example.LossMask);
    }

    [Fact]
    public void TrainingBuilder_PreservesEveryTransitionAcrossChunks()
    {
        const string text =
            "0123456789ABCDEF";

        var tokenizer =
            new FullWorthByteTokenizer();

        var builder =
            new FullWorthNextTokenTrainingSetBuilder(
                tokenizer,
                CreateArchitecture(contextLength: 4));

        int[] encoded =
            tokenizer.Encode(text);

        IReadOnlyList<FullWorthNextTokenTrainingExample> examples =
            builder.Build(text);

        var actualInputs =
            new List<int>();

        var actualTargets =
            new List<int>();

        foreach (FullWorthNextTokenTrainingExample example in examples)
        {
            Assert.Equal(4, example.InputTokens.Length);
            Assert.Equal(4, example.TargetTokens.Length);
            Assert.Equal(4, example.AttentionMask.Length);
            Assert.Equal(4, example.LossMask.Length);

            for (int index = 0;
                 index < example.LossMask.Length;
                 index++)
            {
                if (example.LossMask[index])
                {
                    Assert.True(example.AttentionMask[index]);

                    actualInputs.Add(
                        example.InputTokens[index]);

                    actualTargets.Add(
                        example.TargetTokens[index]);
                }
                else
                {
                    Assert.False(example.AttentionMask[index]);

                    Assert.Equal(
                        FullWorthByteTokenizer.PaddingToken,
                        example.InputTokens[index]);

                    Assert.Equal(
                        FullWorthByteTokenizer.PaddingToken,
                        example.TargetTokens[index]);
                }
            }
        }

        Assert.Equal(
            encoded[..^1],
            actualInputs);

        Assert.Equal(
            encoded[1..],
            actualTargets);

        Assert.Equal(
            encoded.Length - 1,
            actualTargets.Count);
    }

    [Fact]
    public void TrainingBuilder_RejectsNullDependenciesAndNullText()
    {
        var architecture =
            CreateArchitecture(contextLength: 4);

        Assert.Throws<ArgumentNullException>(() =>
            new FullWorthNextTokenTrainingSetBuilder(
                null!,
                architecture));

        Assert.Throws<ArgumentNullException>(() =>
            new FullWorthNextTokenTrainingSetBuilder(
                new FullWorthByteTokenizer(),
                null!));

        var builder =
            new FullWorthNextTokenTrainingSetBuilder(
                new FullWorthByteTokenizer(),
                architecture);

        Assert.Throws<ArgumentNullException>(() =>
            builder.Build(null!));
    }

    private static FullWorthLanguageModelArchitecture CreateArchitecture(
        int contextLength)
    {
        return new FullWorthLanguageModelArchitecture(
            vocabularySize:
                FullWorthByteTokenizer.VocabularySize,
            contextLength:
                contextLength,
            hiddenSize:
                64,
            layerCount:
                2,
            attentionHeadCount:
                4,
            keyValueHeadCount:
                2,
            feedForwardSize:
                128);
    }
}

using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthReferenceDecoderTests
{
    [Fact]
    public void ForwardPass_IsDeterministicFiniteAndVocabularySized()
    {
        var architecture =
            CreateArchitecture();

        FullWorthInitializedModelState state =
            CreateState(
                architecture,
                seed: 123);

        var decoder =
            new FullWorthReferenceDecoder(
                architecture,
                state);

        int[] tokens =
        [
            FullWorthByteTokenizer.BeginToken,
            'A' + FullWorthByteTokenizer.ByteOffset,
            'B' + FullWorthByteTokenizer.ByteOffset
        ];

        float[] first =
            decoder.ForwardNextTokenLogits(tokens);

        float[] second =
            decoder.ForwardNextTokenLogits(tokens);

        Assert.Equal(
            architecture.VocabularySize,
            first.Length);

        Assert.True(
            first.SequenceEqual(second));

        Assert.All(
            first,
            value => Assert.True(float.IsFinite(value)));

        Assert.Contains(
            first,
            value => value != 0f);
    }

    [Fact]
    public void CausalAttention_PreservesPrefixLogitsWhenFutureTokensAreAdded()
    {
        var architecture =
            CreateArchitecture();

        var decoder =
            new FullWorthReferenceDecoder(
                architecture,
                CreateState(
                    architecture,
                    seed: 456));

        int begin =
            FullWorthByteTokenizer.BeginToken;

        int a =
            'A' +
            FullWorthByteTokenizer.ByteOffset;

        int b =
            'B' +
            FullWorthByteTokenizer.ByteOffset;

        float[][] prefix =
            decoder.ForwardAllTokenLogits(
                [begin, a]);

        float[][] extended =
            decoder.ForwardAllTokenLogits(
                [begin, a, b]);

        Assert.True(
            prefix[0].SequenceEqual(
                extended[0]));

        Assert.True(
            prefix[1].SequenceEqual(
                extended[1]));
    }

    [Fact]
    public void Attention_ChangesPredictionWhenPriorContextChanges()
    {
        var architecture =
            CreateArchitecture();

        var decoder =
            new FullWorthReferenceDecoder(
                architecture,
                CreateState(
                    architecture,
                    seed: 789));

        int begin =
            FullWorthByteTokenizer.BeginToken;

        int contextToken =
            'A' +
            FullWorthByteTokenizer.ByteOffset;

        int currentToken =
            'X' +
            FullWorthByteTokenizer.ByteOffset;

        float[] shortContext =
            decoder.ForwardNextTokenLogits(
                [begin, currentToken]);

        float[] longerContext =
            decoder.ForwardNextTokenLogits(
                [begin, contextToken, currentToken]);

        Assert.False(
            shortContext.SequenceEqual(
                longerContext));
    }

    [Fact]
    public void NextTokenLogits_AreLastPositionFromFullForwardPass()
    {
        var architecture =
            CreateArchitecture();

        var decoder =
            new FullWorthReferenceDecoder(
                architecture,
                CreateState(
                    architecture,
                    seed: 111));

        int[] tokens =
        [
            FullWorthByteTokenizer.BeginToken,
            '4' + FullWorthByteTokenizer.ByteOffset,
            '2' + FullWorthByteTokenizer.ByteOffset
        ];

        float[][] all =
            decoder.ForwardAllTokenLogits(tokens);

        float[] next =
            decoder.ForwardNextTokenLogits(tokens);

        Assert.True(
            all[^1].SequenceEqual(next));
    }

    [Fact]
    public void ForwardPass_RejectsPaddingOutOfRangeAndOversizedInput()
    {
        var architecture =
            CreateArchitecture();

        var decoder =
            new FullWorthReferenceDecoder(
                architecture,
                CreateState(
                    architecture,
                    seed: 222));

        Assert.Throws<ArgumentException>(() =>
            decoder.ForwardNextTokenLogits(
                Array.Empty<int>()));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decoder.ForwardNextTokenLogits(
                new[]
                {
                    FullWorthByteTokenizer.PaddingToken
                }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            decoder.ForwardNextTokenLogits(
                new[]
                {
                    architecture.VocabularySize
                }));

        int[] tooLong =
            Enumerable
                .Repeat(
                    FullWorthByteTokenizer.BeginToken,
                    architecture.ContextLength + 1)
                .ToArray();

        Assert.Throws<ArgumentException>(() =>
            decoder.ForwardNextTokenLogits(
                tooLong));
    }

    [Fact]
    public void Decoder_RejectsStateFromDifferentArchitecture()
    {
        FullWorthLanguageModelArchitecture first =
            CreateArchitecture();

        var second =
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength:
                    12,
                hiddenSize:
                    64,
                layerCount:
                    1,
                attentionHeadCount:
                    4,
                keyValueHeadCount:
                    2,
                feedForwardSize:
                    128);

        FullWorthInitializedModelState state =
            CreateState(
                first,
                seed: 333);

        Assert.Throws<ArgumentException>(() =>
            new FullWorthReferenceDecoder(
                second,
                state));
    }

    [Fact]
    public void Architecture_RejectsOddRopeHeadDimension()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength:
                    8,
                hiddenSize:
                    66,
                layerCount:
                    1,
                attentionHeadCount:
                    2,
                keyValueHeadCount:
                    1,
                feedForwardSize:
                    132));
    }

    private static FullWorthInitializedModelState CreateState(
        FullWorthLanguageModelArchitecture architecture,
        long seed)
    {
        long parameters =
            architecture.EstimateParameterCount();

        return FullWorthInitializedModelState.Create(
            architecture,
            masterSeed: seed,
            maxParameterCount: parameters);
    }

    private static FullWorthLanguageModelArchitecture CreateArchitecture()
    {
        return new FullWorthLanguageModelArchitecture(
            vocabularySize:
                FullWorthByteTokenizer.VocabularySize,
            contextLength:
                8,
            hiddenSize:
                64,
            layerCount:
                1,
            attentionHeadCount:
                4,
            keyValueHeadCount:
                2,
            feedForwardSize:
                128);
    }
}

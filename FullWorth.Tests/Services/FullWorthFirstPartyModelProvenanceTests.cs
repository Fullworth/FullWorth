using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthFirstPartyModelProvenanceTests
{
    [Fact]
    public void Initializer_IsReproducibleAndTraversalIndependent()
    {
        var architecture =
            CreateArchitecture();

        var initializer =
            new FullWorthModelInitializer(
                architecture);

        var first =
            new float[128];

        var second =
            new float[128];

        var unrelated =
            new float[128];

        initializer.Fill(
            first,
            "layers.0.attention.query",
            FullWorthModelTensorRole.AttentionInputProjection,
            masterSeed: 42);

        initializer.Fill(
            unrelated,
            "layers.1.feedForward.up",
            FullWorthModelTensorRole.FeedForwardInputProjection,
            masterSeed: 42);

        initializer.Fill(
            second,
            "layers.0.attention.query",
            FullWorthModelTensorRole.AttentionInputProjection,
            masterSeed: 42);

        Assert.Equal(first, second);

        Assert.Contains(
            first,
            value => value != 0f);

        Assert.NotEqual(
            first,
            unrelated);
    }

    [Fact]
    public void Initializer_SeparatesSeedsTensorNamesAndRoles()
    {
        var initializer =
            new FullWorthModelInitializer(
                CreateArchitecture());

        float[] baseline =
            Initialize(
                initializer,
                "layers.0.attention.query",
                FullWorthModelTensorRole.AttentionInputProjection,
                7);

        float[] differentSeed =
            Initialize(
                initializer,
                "layers.0.attention.query",
                FullWorthModelTensorRole.AttentionInputProjection,
                8);

        float[] differentName =
            Initialize(
                initializer,
                "layers.0.attention.key",
                FullWorthModelTensorRole.AttentionInputProjection,
                7);

        float[] differentRole =
            Initialize(
                initializer,
                "layers.0.attention.query",
                FullWorthModelTensorRole.FeedForwardInputProjection,
                7);

        Assert.NotEqual(baseline, differentSeed);
        Assert.NotEqual(baseline, differentName);
        Assert.NotEqual(baseline, differentRole);
    }

    [Fact]
    public void Initializer_UsesUnitRmsNormAndScaledResidualOutputs()
    {
        var architecture =
            CreateArchitecture();

        var initializer =
            new FullWorthModelInitializer(
                architecture);

        var normalization =
            new float[32];

        initializer.Fill(
            normalization,
            "layers.0.attentionNorm",
            FullWorthModelTensorRole.NormalizationScale,
            masterSeed: 1234);

        Assert.All(
            normalization,
            value => Assert.Equal(1f, value));

        Assert.Equal(
            FullWorthModelInitializer.BaseStandardDeviation,
            initializer.GetStandardDeviation(
                FullWorthModelTensorRole.TokenEmbedding));

        double expectedResidualStdDev =
            FullWorthModelInitializer.BaseStandardDeviation /
            Math.Sqrt(2d * architecture.LayerCount);

        Assert.Equal(
            expectedResidualStdDev,
            initializer.GetStandardDeviation(
                FullWorthModelTensorRole.AttentionOutputProjection),
            precision: 12);

        Assert.Equal(
            expectedResidualStdDev,
            initializer.GetStandardDeviation(
                FullWorthModelTensorRole.FeedForwardOutputProjection),
            precision: 12);
    }

    [Fact]
    public void Initializer_RejectsInvalidTensorRequests()
    {
        var initializer =
            new FullWorthModelInitializer(
                CreateArchitecture());

        Assert.Throws<ArgumentException>(() =>
            initializer.Fill(
                Span<float>.Empty,
                "weights",
                FullWorthModelTensorRole.TokenEmbedding,
                masterSeed: 1));

        Assert.Throws<ArgumentException>(() =>
            initializer.Fill(
                new float[1],
                " ",
                FullWorthModelTensorRole.TokenEmbedding,
                masterSeed: 1));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            initializer.Fill(
                new float[1],
                "weights",
                (FullWorthModelTensorRole)999,
                masterSeed: 1));
    }

    [Fact]
    public void InitialCheckpointManifest_BindsWeightsToArchitectureAndTokenizer()
    {
        var architecture =
            CreateArchitecture();

        const string weightsSha256 =
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        DateTimeOffset created =
            new(
                2026,
                9,
                26,
                6,
                0,
                0,
                TimeSpan.FromHours(-6));

        FullWorthModelCheckpointManifest manifest =
            FullWorthModelCheckpointManifest.CreateInitial(
                architecture,
                masterSeed: 8675309,
                weightsSha256,
                created);

        manifest.ValidateFor(architecture);

        Assert.Equal(
            FullWorthModelCheckpointManifest.CurrentFormatVersion,
            manifest.FormatVersion);

        Assert.Equal(
            FullWorthModelInitializer.Version,
            manifest.InitializationVersion);

        Assert.Equal(
            architecture.CompatibilityId,
            manifest.ArchitectureCompatibilityId);

        Assert.Equal(
            architecture.EstimateParameterCount(),
            manifest.ParameterCount);

        Assert.Equal(0, manifest.TrainingStep);
        Assert.Equal(0, manifest.SeenTokenCount);
        Assert.Null(manifest.OptimizerStateSha256);
        Assert.Equal(TimeSpan.Zero, manifest.CreatedAtUtc.Offset);
        Assert.Equal(weightsSha256, manifest.WeightsSha256);
    }

    [Fact]
    public void CheckpointManifest_FailsClosedForWrongArchitectureOrDigest()
    {
        var architecture =
            CreateArchitecture();

        FullWorthModelCheckpointManifest manifest =
            FullWorthModelCheckpointManifest.CreateInitial(
                architecture,
                masterSeed: 1,
                weightsSha256:
                    "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                createdAtUtc:
                    DateTimeOffset.UtcNow);

        var incompatible =
            new FullWorthLanguageModelArchitecture(
                vocabularySize:
                    FullWorthByteTokenizer.VocabularySize,
                contextLength:
                    9,
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

        Assert.Throws<InvalidDataException>(() =>
            manifest.ValidateFor(incompatible));

        FullWorthModelCheckpointManifest badDigest =
            manifest with
            {
                WeightsSha256 =
                    "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"
            };

        Assert.Throws<InvalidDataException>(() =>
            badDigest.ValidateFor(architecture));
    }

    [Fact]
    public void TrainedCheckpoint_RequiresTokenAndOptimizerProvenance()
    {
        var architecture =
            CreateArchitecture();

        FullWorthModelCheckpointManifest initial =
            FullWorthModelCheckpointManifest.CreateInitial(
                architecture,
                masterSeed: 1,
                weightsSha256:
                    "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                createdAtUtc:
                    DateTimeOffset.UtcNow);

        FullWorthModelCheckpointManifest missingOptimizer =
            initial with
            {
                TrainingStep = 10,
                SeenTokenCount = 8_192
            };

        Assert.Throws<InvalidDataException>(() =>
            missingOptimizer.ValidateFor(architecture));

        FullWorthModelCheckpointManifest resumable =
            missingOptimizer with
            {
                OptimizerStateSha256 =
                    "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
            };

        resumable.ValidateFor(architecture);
    }

    private static float[] Initialize(
        FullWorthModelInitializer initializer,
        string name,
        FullWorthModelTensorRole role,
        long seed)
    {
        var values =
            new float[64];

        initializer.Fill(
            values,
            name,
            role,
            seed);

        return values;
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
                2,
            attentionHeadCount:
                4,
            keyValueHeadCount:
                2,
            feedForwardSize:
                128);
    }
}

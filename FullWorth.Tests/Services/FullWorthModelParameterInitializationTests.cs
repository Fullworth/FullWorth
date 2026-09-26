using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthModelParameterInitializationTests
{
    [Fact]
    public void ParameterLayout_CoversExactArchitectureParameterCount()
    {
        FullWorthLanguageModelArchitecture architecture =
            FullWorthLanguageModelArchitecture.CreateBootstrap();

        FullWorthModelParameterLayout layout =
            FullWorthModelParameterLayout.Create(architecture);

        Assert.Equal(
            architecture.EstimateParameterCount(),
            layout.TotalParameterCount);

        Assert.Equal(
            2 + (architecture.LayerCount * 9),
            layout.Tensors.Count);

        Assert.Equal(
            layout.Tensors.Count,
            layout.Tensors
                .Select(tensor => tensor.Name)
                .Distinct(StringComparer.Ordinal)
                .Count());

        Assert.DoesNotContain(
            layout.Tensors,
            tensor =>
                tensor.Name.Contains(
                    "lm_head",
                    StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            layout.Tensors,
            tensor =>
                tensor.Name.Contains(
                    "position",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ParameterLayout_UsesGroupedQueryAttentionShapes()
    {
        FullWorthLanguageModelArchitecture architecture =
            FullWorthLanguageModelArchitecture.CreateBootstrap();

        FullWorthModelParameterLayout layout =
            FullWorthModelParameterLayout.Create(architecture);

        FullWorthModelParameterTensor query =
            Assert.Single(
                layout.Tensors,
                tensor =>
                    tensor.Name ==
                    "layers.0.attention.q_proj.weight");

        FullWorthModelParameterTensor key =
            Assert.Single(
                layout.Tensors,
                tensor =>
                    tensor.Name ==
                    "layers.0.attention.k_proj.weight");

        FullWorthModelParameterTensor value =
            Assert.Single(
                layout.Tensors,
                tensor =>
                    tensor.Name ==
                    "layers.0.attention.v_proj.weight");

        Assert.Equal(
            new[]
            {
                architecture.HiddenSize,
                architecture.HiddenSize
            },
            query.Shape);

        int expectedKeyValueWidth =
            architecture.HeadSize *
            architecture.KeyValueHeadCount;

        Assert.Equal(
            new[]
            {
                expectedKeyValueWidth,
                architecture.HiddenSize
            },
            key.Shape);

        Assert.Equal(
            key.Shape,
            value.Shape);
    }

    [Fact]
    public void InitializationPlan_DerivesUniqueStableTensorSeeds()
    {
        FullWorthLanguageModelArchitecture architecture =
            FullWorthLanguageModelArchitecture.CreateBootstrap();

        FullWorthModelParameterLayout layout =
            FullWorthModelParameterLayout.Create(architecture);

        var first =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 0x1234_5678_9ABC_DEF0UL);

        var second =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 0x1234_5678_9ABC_DEF0UL);

        ulong[] seeds =
            layout.Tensors
                .Select(
                    tensor =>
                        first.DeriveTensorSeed(
                            tensor.Name))
                .ToArray();

        Assert.Equal(
            seeds.Length,
            seeds.Distinct().Count());

        Assert.Equal(
            first.InitializationId,
            second.InitializationId);

        Assert.Equal(
            first.ArchitectureCompatibilityId,
            architecture.CompatibilityId);

        Assert.Equal(
            first.TokenizerVersion,
            FullWorthByteTokenizer.Version);

        foreach (FullWorthModelParameterTensor tensor in layout.Tensors)
        {
            Assert.Equal(
                first.DeriveTensorSeed(tensor.Name),
                second.DeriveTensorSeed(tensor.Name));
        }
    }

    [Fact]
    public void InitializationPlan_ChangesIdentityWhenRootSeedChanges()
    {
        FullWorthLanguageModelArchitecture architecture =
            FullWorthLanguageModelArchitecture.CreateBootstrap();

        var first =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 1UL);

        var second =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 2UL);

        Assert.NotEqual(
            first.InitializationId,
            second.InitializationId);

        Assert.NotEqual(
            first.DeriveTensorSeed(
                "token_embedding.weight"),
            second.DeriveTensorSeed(
                "token_embedding.weight"));
    }

    [Fact]
    public void InitializationPlan_UsesOnesNormalAndResidualScaledNormal()
    {
        FullWorthLanguageModelArchitecture architecture =
            FullWorthLanguageModelArchitecture.CreateBootstrap();

        FullWorthModelParameterLayout layout =
            FullWorthModelParameterLayout.Create(architecture);

        var plan =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 42UL);

        FullWorthTensorInitializationSpec embedding =
            plan.CreateSpec(
                Assert.Single(
                    layout.Tensors,
                    tensor =>
                        tensor.Name ==
                        "token_embedding.weight"));

        FullWorthTensorInitializationSpec norm =
            plan.CreateSpec(
                Assert.Single(
                    layout.Tensors,
                    tensor =>
                        tensor.Name ==
                        "layers.0.attention_norm.weight"));

        FullWorthTensorInitializationSpec residual =
            plan.CreateSpec(
                Assert.Single(
                    layout.Tensors,
                    tensor =>
                        tensor.Name ==
                        "layers.0.attention.o_proj.weight"));

        Assert.Equal(
            FullWorthParameterInitializationKind.Normal,
            embedding.Kind);

        Assert.Equal(
            FullWorthModelInitializationPlan.BaseStandardDeviation,
            embedding.StandardDeviation);

        Assert.Equal(
            FullWorthParameterInitializationKind.Ones,
            norm.Kind);

        Assert.Equal(1f, norm.Mean);
        Assert.Equal(0f, norm.StandardDeviation);

        Assert.Equal(
            FullWorthParameterInitializationKind.ResidualScaledNormal,
            residual.Kind);

        Assert.InRange(
            residual.StandardDeviation,
            0f,
            embedding.StandardDeviation);

        Assert.True(
            residual.StandardDeviation > 0f);
    }

    [Fact]
    public void ReferenceRandomInitializer_IsExactlyReproducible()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateSmallArchitecture();

        var plan =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 987_654_321UL);

        FullWorthModelParameterTensor tensor =
            Assert.Single(
                FullWorthModelParameterLayout
                    .Create(architecture)
                    .Tensors,
                candidate =>
                    candidate.Name ==
                    "token_embedding.weight");

        FullWorthTensorInitializationSpec specification =
            plan.CreateSpec(tensor);

        float[] first =
            new float[64];

        float[] second =
            new float[64];

        FullWorthReferenceRandomInitializer.Fill(
            first,
            specification);

        FullWorthReferenceRandomInitializer.Fill(
            second,
            specification);

        Assert.Equal(
            first,
            second);

        Assert.All(
            first,
            value =>
                Assert.True(
                    float.IsFinite(value)));

        Assert.Contains(
            first,
            value => value != 0f);
    }

    [Fact]
    public void ReferenceRandomInitializer_DifferentTensorSeedsProduceDifferentWeights()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateSmallArchitecture();

        FullWorthModelParameterLayout layout =
            FullWorthModelParameterLayout.Create(architecture);

        var plan =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 123UL);

        FullWorthTensorInitializationSpec firstSpec =
            plan.CreateSpec(
                Assert.Single(
                    layout.Tensors,
                    tensor =>
                        tensor.Name ==
                        "layers.0.attention.q_proj.weight"));

        FullWorthTensorInitializationSpec secondSpec =
            plan.CreateSpec(
                Assert.Single(
                    layout.Tensors,
                    tensor =>
                        tensor.Name ==
                        "layers.0.attention.k_proj.weight"));

        float[] first =
            new float[32];

        float[] second =
            new float[32];

        FullWorthReferenceRandomInitializer.Fill(
            first,
            firstSpec);

        FullWorthReferenceRandomInitializer.Fill(
            second,
            secondSpec);

        Assert.False(
            first.SequenceEqual(second));
    }

    [Fact]
    public void ReferenceRandomInitializer_OnesAreExact()
    {
        var values =
            new float[32];

        FullWorthReferenceRandomInitializer.Fill(
            values,
            new FullWorthTensorInitializationSpec(
                FullWorthParameterInitializationKind.Ones,
                Seed: 123UL,
                Mean: 1f,
                StandardDeviation: 0f));

        Assert.All(
            values,
            value =>
                Assert.Equal(1f, value));
    }

    [Fact]
    public void InitializationPlan_RejectsBlankTensorNames()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateSmallArchitecture();

        var plan =
            new FullWorthModelInitializationPlan(
                architecture,
                rootSeed: 0UL);

        Assert.Throws<ArgumentException>(() =>
            plan.DeriveTensorSeed(" "));

        Assert.Throws<ArgumentNullException>(() =>
            plan.CreateSpec(null!));
    }

    private static FullWorthLanguageModelArchitecture CreateSmallArchitecture()
    {
        return new FullWorthLanguageModelArchitecture(
            vocabularySize:
                FullWorthByteTokenizer.VocabularySize,
            contextLength:
                16,
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

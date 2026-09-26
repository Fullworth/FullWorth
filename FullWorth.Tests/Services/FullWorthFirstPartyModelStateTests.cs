using FullWorth.API.Services.Intelligence;

namespace FullWorth.Tests.Services;

public sealed class FullWorthFirstPartyModelStateTests
{
    [Fact]
    public void TensorLayout_CoversEveryTrainableParameterExactlyOnce()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        var layout =
            new FullWorthModelTensorLayout(
                architecture);

        Assert.Equal(
            architecture.EstimateParameterCount(),
            layout.ParameterCount);

        Assert.Equal(
            (architecture.LayerCount * 9) + 2,
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
                string.Equals(
                    tensor.Name,
                    FullWorthModelTensorLayout.OutputProjectionAliasName,
                    StringComparison.Ordinal));

        Assert.Equal(
            FullWorthModelTensorLayout.TokenEmbeddingTensorName,
            layout.OutputProjectionSourceTensorName);
    }

    [Fact]
    public void TensorLayout_UsesGroupedQueryAndSwiGluShapes()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        var layout =
            new FullWorthModelTensorLayout(
                architecture);

        int keyValueWidth =
            architecture.HeadSize *
            architecture.KeyValueHeadCount;

        AssertTensor(
            layout,
            "tokenEmbedding.weight",
            rows: architecture.VocabularySize,
            columns: architecture.HiddenSize,
            FullWorthModelTensorRole.TokenEmbedding);

        AssertTensor(
            layout,
            "layers.0.attention.query.weight",
            rows: architecture.HiddenSize,
            columns: architecture.HiddenSize,
            FullWorthModelTensorRole.AttentionInputProjection);

        AssertTensor(
            layout,
            "layers.0.attention.key.weight",
            rows: keyValueWidth,
            columns: architecture.HiddenSize,
            FullWorthModelTensorRole.AttentionInputProjection);

        AssertTensor(
            layout,
            "layers.0.attention.value.weight",
            rows: keyValueWidth,
            columns: architecture.HiddenSize,
            FullWorthModelTensorRole.AttentionInputProjection);

        AssertTensor(
            layout,
            "layers.0.feedForward.gate.weight",
            rows: architecture.FeedForwardSize,
            columns: architecture.HiddenSize,
            FullWorthModelTensorRole.FeedForwardInputProjection);

        AssertTensor(
            layout,
            "layers.0.feedForward.up.weight",
            rows: architecture.FeedForwardSize,
            columns: architecture.HiddenSize,
            FullWorthModelTensorRole.FeedForwardInputProjection);

        AssertTensor(
            layout,
            "layers.0.feedForward.down.weight",
            rows: architecture.HiddenSize,
            columns: architecture.FeedForwardSize,
            FullWorthModelTensorRole.FeedForwardOutputProjection);

        FullWorthModelTensorSpec norm =
            layout.GetRequired(
                "layers.0.attentionNorm.weight");

        Assert.True(norm.IsVector);
        Assert.Equal(architecture.HiddenSize, norm.Rows);
        Assert.Equal(1, norm.Columns);
    }

    [Fact]
    public void InitializedState_IsReproducibleCompleteAndCheckpointable()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        long parameterCount =
            architecture.EstimateParameterCount();

        FullWorthInitializedModelState first =
            FullWorthInitializedModelState.Create(
                architecture,
                masterSeed: 2468,
                maxParameterCount: parameterCount);

        FullWorthInitializedModelState second =
            FullWorthInitializedModelState.Create(
                architecture,
                masterSeed: 2468,
                maxParameterCount: parameterCount);

        Assert.Equal(
            first.WeightsSha256,
            second.WeightsSha256);

        Assert.Equal(
            64,
            first.WeightsSha256.Length);

        foreach (FullWorthModelTensorSpec tensor in first.Layout.Tensors)
        {
            ReadOnlyMemory<float> weights =
                first.GetRequiredTensor(
                    tensor.Name);

            Assert.Equal(
                tensor.ElementCount,
                (long)weights.Length);
        }

        Assert.All(
            first
                .GetRequiredTensor(
                    "layers.0.attentionNorm.weight")
                .ToArray(),
            value => Assert.Equal(1f, value));

        FullWorthModelCheckpointManifest manifest =
            first.CreateInitialCheckpointManifest(
                new DateTimeOffset(
                    2026,
                    9,
                    26,
                    7,
                    0,
                    0,
                    TimeSpan.Zero));

        manifest.ValidateFor(architecture);

        Assert.Equal(
            first.WeightsSha256,
            manifest.WeightsSha256);

        Assert.Equal(
            2468,
            manifest.MasterSeed);
    }

    [Fact]
    public void InitializedState_ChangesDigestWhenMasterSeedChanges()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        long budget =
            architecture.EstimateParameterCount();

        FullWorthInitializedModelState first =
            FullWorthInitializedModelState.Create(
                architecture,
                masterSeed: 1,
                maxParameterCount: budget);

        FullWorthInitializedModelState second =
            FullWorthInitializedModelState.Create(
                architecture,
                masterSeed: 2,
                maxParameterCount: budget);

        Assert.NotEqual(
            first.WeightsSha256,
            second.WeightsSha256);
    }

    [Fact]
    public void InitializedState_RequiresExplicitMemoryBudget()
    {
        FullWorthLanguageModelArchitecture architecture =
            CreateArchitecture();

        long required =
            architecture.EstimateParameterCount();

        Assert.Throws<InvalidOperationException>(() =>
            FullWorthInitializedModelState.Create(
                architecture,
                masterSeed: 1,
                maxParameterCount: required - 1));

        FullWorthInitializedModelState state =
            FullWorthInitializedModelState.Create(
                architecture,
                masterSeed: 1,
                maxParameterCount: required);

        Assert.Equal(
            required,
            state.Layout.ParameterCount);
    }

    [Fact]
    public void TensorLookup_FailsClosedForUnknownName()
    {
        var layout =
            new FullWorthModelTensorLayout(
                CreateArchitecture());

        Assert.Throws<KeyNotFoundException>(() =>
            layout.GetRequired(
                "layers.99.attention.query.weight"));

        FullWorthInitializedModelState state =
            FullWorthInitializedModelState.Create(
                layout.Architecture,
                masterSeed: 1,
                maxParameterCount:
                    layout.ParameterCount);

        Assert.Throws<KeyNotFoundException>(() =>
            state.GetRequiredTensor(
                "lmHead.weight"));
    }

    private static void AssertTensor(
        FullWorthModelTensorLayout layout,
        string name,
        int rows,
        int columns,
        FullWorthModelTensorRole role)
    {
        FullWorthModelTensorSpec tensor =
            layout.GetRequired(name);

        Assert.Equal(rows, tensor.Rows);
        Assert.Equal(columns, tensor.Columns);
        Assert.Equal(role, tensor.Role);
        Assert.Equal(
            (long)rows * columns,
            tensor.ElementCount);
    }

    private static FullWorthLanguageModelArchitecture CreateArchitecture()
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

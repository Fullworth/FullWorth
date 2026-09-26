namespace FullWorth.API.Services.Intelligence;

public sealed record FullWorthModelTensorSpec
{
    public FullWorthModelTensorSpec(
        string name,
        FullWorthModelTensorRole role,
        int rows,
        int columns)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "A stable tensor name is required.",
                nameof(name));

        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        if (rows < 1)
            throw new ArgumentOutOfRangeException(nameof(rows));

        if (columns < 1)
            throw new ArgumentOutOfRangeException(nameof(columns));

        Name = name;
        Role = role;
        Rows = rows;
        Columns = columns;
    }

    public string Name { get; }

    public FullWorthModelTensorRole Role { get; }

    public int Rows { get; }

    public int Columns { get; }

    public long ElementCount =>
        checked((long)Rows * Columns);

    public bool IsVector =>
        Columns == 1;
}

/// <summary>
/// Complete named tensor layout for the FullWorth decoder architecture.
/// The language-model output projection is tied to the token embedding and
/// therefore is intentionally not represented as a second trainable tensor.
/// </summary>
public sealed class FullWorthModelTensorLayout
{
    public const string TokenEmbeddingTensorName =
        "tokenEmbedding.weight";

    public const string OutputProjectionAliasName =
        "lmHead.weight";

    private readonly Dictionary<string, FullWorthModelTensorSpec>
        _byName;

    public FullWorthModelTensorLayout(
        FullWorthLanguageModelArchitecture architecture)
    {
        Architecture =
            architecture ??
            throw new ArgumentNullException(nameof(architecture));

        Tensors =
            BuildTensors(architecture);

        _byName =
            Tensors.ToDictionary(
                tensor => tensor.Name,
                StringComparer.Ordinal);

        ParameterCount =
            Tensors.Sum(
                tensor => tensor.ElementCount);

        long expected =
            architecture.EstimateParameterCount();

        if (ParameterCount != expected)
            throw new InvalidOperationException(
                "The tensor layout does not match the architecture parameter count.");
    }

    public FullWorthLanguageModelArchitecture Architecture { get; }

    public IReadOnlyList<FullWorthModelTensorSpec> Tensors { get; }

    public long ParameterCount { get; }

    public string OutputProjectionSourceTensorName =>
        TokenEmbeddingTensorName;

    public FullWorthModelTensorSpec GetRequired(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _byName.TryGetValue(
            name,
            out FullWorthModelTensorSpec? tensor)
            ? tensor
            : throw new KeyNotFoundException(
                "The requested model tensor does not exist.");
    }

    private static IReadOnlyList<FullWorthModelTensorSpec> BuildTensors(
        FullWorthLanguageModelArchitecture architecture)
    {
        var tensors =
            new List<FullWorthModelTensorSpec>(
                capacity:
                    checked((architecture.LayerCount * 9) + 2));

        int hidden =
            architecture.HiddenSize;

        int keyValueWidth =
            checked(
                architecture.HeadSize *
                architecture.KeyValueHeadCount);

        int feedForward =
            architecture.FeedForwardSize;

        tensors.Add(
            Matrix(
                TokenEmbeddingTensorName,
                FullWorthModelTensorRole.TokenEmbedding,
                architecture.VocabularySize,
                hidden));

        for (int layer = 0;
             layer < architecture.LayerCount;
             layer++)
        {
            string prefix =
                $"layers.{layer}";

            tensors.Add(
                Vector(
                    $"{prefix}.attentionNorm.weight",
                    FullWorthModelTensorRole.NormalizationScale,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.attention.query.weight",
                    FullWorthModelTensorRole.AttentionInputProjection,
                    hidden,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.attention.key.weight",
                    FullWorthModelTensorRole.AttentionInputProjection,
                    keyValueWidth,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.attention.value.weight",
                    FullWorthModelTensorRole.AttentionInputProjection,
                    keyValueWidth,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.attention.output.weight",
                    FullWorthModelTensorRole.AttentionOutputProjection,
                    hidden,
                    hidden));

            tensors.Add(
                Vector(
                    $"{prefix}.feedForwardNorm.weight",
                    FullWorthModelTensorRole.NormalizationScale,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.feedForward.gate.weight",
                    FullWorthModelTensorRole.FeedForwardInputProjection,
                    feedForward,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.feedForward.up.weight",
                    FullWorthModelTensorRole.FeedForwardInputProjection,
                    feedForward,
                    hidden));

            tensors.Add(
                Matrix(
                    $"{prefix}.feedForward.down.weight",
                    FullWorthModelTensorRole.FeedForwardOutputProjection,
                    hidden,
                    feedForward));
        }

        tensors.Add(
            Vector(
                "finalNorm.weight",
                FullWorthModelTensorRole.NormalizationScale,
                hidden));

        return tensors;
    }

    private static FullWorthModelTensorSpec Matrix(
        string name,
        FullWorthModelTensorRole role,
        int rows,
        int columns) =>
        new(
            name,
            role,
            rows,
            columns);

    private static FullWorthModelTensorSpec Vector(
        string name,
        FullWorthModelTensorRole role,
        int length) =>
        new(
            name,
            role,
            length,
            columns: 1);
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FullWorth.API.Services.Intelligence;

public enum FullWorthParameterInitializationKind
{
    Normal = 0,
    ResidualScaledNormal = 1,
    Ones = 2
}

/// <summary>
/// One named trainable tensor in the versioned FullWorth model.
/// Names and shapes are checkpoint identity and must not be changed silently.
/// </summary>
public sealed record FullWorthModelParameterTensor(
    string Name,
    IReadOnlyList<int> Shape,
    long ElementCount,
    FullWorthParameterInitializationKind InitializationKind);

/// <summary>
/// Complete trainable tensor layout for the v1 decoder architecture.
/// The tied language-model output head deliberately has no second weight tensor.
/// </summary>
public sealed class FullWorthModelParameterLayout
{
    private FullWorthModelParameterLayout(
        IReadOnlyList<FullWorthModelParameterTensor> tensors,
        long totalParameterCount)
    {
        Tensors = tensors;
        TotalParameterCount = totalParameterCount;
    }

    public IReadOnlyList<FullWorthModelParameterTensor> Tensors { get; }

    public long TotalParameterCount { get; }

    public static FullWorthModelParameterLayout Create(
        FullWorthLanguageModelArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(architecture);

        var tensors =
            new List<FullWorthModelParameterTensor>();

        Add(
            tensors,
            "token_embedding.weight",
            FullWorthParameterInitializationKind.Normal,
            architecture.VocabularySize,
            architecture.HiddenSize);

        int keyValueWidth =
            checked(
                architecture.HeadSize *
                architecture.KeyValueHeadCount);

        for (int layer = 0;
             layer < architecture.LayerCount;
             layer++)
        {
            string prefix =
                $"layers.{layer}";

            Add(
                tensors,
                $"{prefix}.attention_norm.weight",
                FullWorthParameterInitializationKind.Ones,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.attention.q_proj.weight",
                FullWorthParameterInitializationKind.Normal,
                architecture.HiddenSize,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.attention.k_proj.weight",
                FullWorthParameterInitializationKind.Normal,
                keyValueWidth,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.attention.v_proj.weight",
                FullWorthParameterInitializationKind.Normal,
                keyValueWidth,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.attention.o_proj.weight",
                FullWorthParameterInitializationKind.ResidualScaledNormal,
                architecture.HiddenSize,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.post_attention_norm.weight",
                FullWorthParameterInitializationKind.Ones,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.ffn.gate_proj.weight",
                FullWorthParameterInitializationKind.Normal,
                architecture.FeedForwardSize,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.ffn.up_proj.weight",
                FullWorthParameterInitializationKind.Normal,
                architecture.FeedForwardSize,
                architecture.HiddenSize);

            Add(
                tensors,
                $"{prefix}.ffn.down_proj.weight",
                FullWorthParameterInitializationKind.ResidualScaledNormal,
                architecture.HiddenSize,
                architecture.FeedForwardSize);
        }

        Add(
            tensors,
            "final_norm.weight",
            FullWorthParameterInitializationKind.Ones,
            architecture.HiddenSize);

        long total =
            tensors.Sum(tensor => tensor.ElementCount);

        long expected =
            architecture.EstimateParameterCount();

        if (total != expected)
        {
            throw new InvalidOperationException(
                "The FullWorth parameter layout does not match the architecture parameter count.");
        }

        if (tensors
            .Select(tensor => tensor.Name)
            .Distinct(StringComparer.Ordinal)
            .Count() != tensors.Count)
        {
            throw new InvalidOperationException(
                "The FullWorth parameter layout contains duplicate tensor names.");
        }

        return new FullWorthModelParameterLayout(
            tensors.AsReadOnly(),
            total);
    }

    private static void Add(
        ICollection<FullWorthModelParameterTensor> tensors,
        string name,
        FullWorthParameterInitializationKind initializationKind,
        params int[] shape)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A tensor name is required.", nameof(name));

        if (shape.Length == 0 ||
            shape.Any(dimension => dimension <= 0))
            throw new ArgumentOutOfRangeException(nameof(shape));

        long elementCount = 1;

        checked
        {
            foreach (int dimension in shape)
                elementCount *= dimension;
        }

        tensors.Add(
            new FullWorthModelParameterTensor(
                name,
                Array.AsReadOnly(shape),
                elementCount,
                initializationKind));
    }
}

public sealed record FullWorthTensorInitializationSpec(
    FullWorthParameterInitializationKind Kind,
    ulong Seed,
    float Mean,
    float StandardDeviation);

/// <summary>
/// Reproducible random-initialization provenance for a FullWorth-owned model.
/// The root seed and tensor names deterministically derive independent tensor
/// seeds. This is model provenance, not authorization to use any training data.
/// </summary>
public sealed class FullWorthModelInitializationPlan
{
    public const string AlgorithmVersion =
        "fullworth-random-init-v1";

    public const float BaseStandardDeviation =
        0.02f;

    private readonly FullWorthLanguageModelArchitecture _architecture;

    public FullWorthModelInitializationPlan(
        FullWorthLanguageModelArchitecture architecture,
        ulong rootSeed)
    {
        _architecture =
            architecture ??
            throw new ArgumentNullException(nameof(architecture));

        RootSeed = rootSeed;
        InitializationId = CreateInitializationId();
    }

    public ulong RootSeed { get; }

    public string ArchitectureCompatibilityId =>
        _architecture.CompatibilityId;

    public string TokenizerVersion =>
        _architecture.TokenizerVersion;

    public string InitializationId { get; }

    public FullWorthTensorInitializationSpec CreateSpec(
        FullWorthModelParameterTensor tensor)
    {
        ArgumentNullException.ThrowIfNull(tensor);

        ulong seed =
            DeriveTensorSeed(tensor.Name);

        return tensor.InitializationKind switch
        {
            FullWorthParameterInitializationKind.Ones =>
                new FullWorthTensorInitializationSpec(
                    tensor.InitializationKind,
                    seed,
                    Mean: 1f,
                    StandardDeviation: 0f),

            FullWorthParameterInitializationKind.Normal =>
                new FullWorthTensorInitializationSpec(
                    tensor.InitializationKind,
                    seed,
                    Mean: 0f,
                    StandardDeviation:
                        BaseStandardDeviation),

            FullWorthParameterInitializationKind.ResidualScaledNormal =>
                new FullWorthTensorInitializationSpec(
                    tensor.InitializationKind,
                    seed,
                    Mean: 0f,
                    StandardDeviation:
                        BaseStandardDeviation /
                        MathF.Sqrt(
                            2f *
                            _architecture.LayerCount)),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(tensor),
                    "Unknown FullWorth parameter initialization kind.")
        };
    }

    public ulong DeriveTensorSeed(
        string tensorName)
    {
        if (string.IsNullOrWhiteSpace(tensorName))
            throw new ArgumentException(
                "A tensor name is required.",
                nameof(tensorName));

        string material =
            string.Join(
                "|",
                AlgorithmVersion,
                _architecture.CompatibilityId,
                RootSeed.ToString(CultureInfo.InvariantCulture),
                tensorName);

        byte[] digest =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(material));

        return
            ((ulong)digest[0] << 56) |
            ((ulong)digest[1] << 48) |
            ((ulong)digest[2] << 40) |
            ((ulong)digest[3] << 32) |
            ((ulong)digest[4] << 24) |
            ((ulong)digest[5] << 16) |
            ((ulong)digest[6] << 8) |
            digest[7];
    }

    private string CreateInitializationId()
    {
        string material =
            string.Join(
                "|",
                AlgorithmVersion,
                _architecture.CompatibilityId,
                RootSeed.ToString(CultureInfo.InvariantCulture),
                _architecture.EstimateParameterCount()
                    .ToString(CultureInfo.InvariantCulture));

        byte[] digest =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(material));

        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}

/// <summary>
/// CPU reference implementation of FullWorth random initialization. It exists
/// to make the algorithm inspectable and testable before a GPU backend is
/// selected. Production training may use an equivalent accelerator kernel,
/// but saved weights—not a provider model—remain the artifact of record.
/// </summary>
public static class FullWorthReferenceRandomInitializer
{
    public static void Fill(
        Span<float> destination,
        FullWorthTensorInitializationSpec specification)
    {
        ArgumentNullException.ThrowIfNull(specification);

        if (specification.Kind ==
            FullWorthParameterInitializationKind.Ones)
        {
            destination.Fill(1f);
            return;
        }

        if (!float.IsFinite(specification.StandardDeviation) ||
            specification.StandardDeviation <= 0f ||
            !float.IsFinite(specification.Mean))
        {
            throw new ArgumentOutOfRangeException(
                nameof(specification),
                "Normal initialization requires finite distribution parameters.");
        }

        ulong state =
            specification.Seed;

        int index = 0;

        while (index < destination.Length)
        {
            double u1 =
                NextOpenUnitInterval(ref state);

            double u2 =
                NextOpenUnitInterval(ref state);

            double radius =
                Math.Sqrt(
                    -2d *
                    Math.Log(u1));

            double angle =
                2d *
                Math.PI *
                u2;

            float first =
                specification.Mean +
                specification.StandardDeviation *
                (float)(
                    radius *
                    Math.Cos(angle));

            destination[index++] =
                first;

            if (index >= destination.Length)
                break;

            float second =
                specification.Mean +
                specification.StandardDeviation *
                (float)(
                    radius *
                    Math.Sin(angle));

            destination[index++] =
                second;
        }
    }

    private static double NextOpenUnitInterval(
        ref ulong state)
    {
        ulong value =
            NextSplitMix64(ref state);

        const double inverse53 =
            1d / 9_007_199_254_740_992d;

        return
            ((value >> 11) + 0.5d) *
            inverse53;
    }

    private static ulong NextSplitMix64(
        ref ulong state)
    {
        state +=
            0x9E3779B97F4A7C15UL;

        ulong value =
            state;

        value =
            (value ^ (value >> 30)) *
            0xBF58476D1CE4E5B9UL;

        value =
            (value ^ (value >> 27)) *
            0x94D049BB133111EBUL;

        return
            value ^
            (value >> 31);
    }
}

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// Explicitly created CPU-resident initial model state for bounded experiments.
/// Production/GPU serving may use a different storage implementation while
/// preserving the same versioned tensor layout and checkpoint contract.
/// </summary>
public sealed class FullWorthInitializedModelState
{
    private readonly Dictionary<string, float[]> _weights;

    private FullWorthInitializedModelState(
        FullWorthModelTensorLayout layout,
        long masterSeed,
        Dictionary<string, float[]> weights,
        string weightsSha256)
    {
        Layout = layout;
        MasterSeed = masterSeed;
        _weights = weights;
        WeightsSha256 = weightsSha256;
    }

    public FullWorthModelTensorLayout Layout { get; }

    public long MasterSeed { get; }

    public string WeightsSha256 { get; }

    public static FullWorthInitializedModelState Create(
        FullWorthLanguageModelArchitecture architecture,
        long masterSeed,
        long maxParameterCount)
    {
        ArgumentNullException.ThrowIfNull(architecture);

        if (maxParameterCount < 1)
            throw new ArgumentOutOfRangeException(
                nameof(maxParameterCount));

        var layout =
            new FullWorthModelTensorLayout(
                architecture);

        if (layout.ParameterCount > maxParameterCount)
            throw new InvalidOperationException(
                "The model exceeds the explicitly approved CPU initialization budget.");

        var initializer =
            new FullWorthModelInitializer(
                architecture);

        var weights =
            new Dictionary<string, float[]>(
                layout.Tensors.Count,
                StringComparer.Ordinal);

        foreach (FullWorthModelTensorSpec tensor in layout.Tensors)
        {
            if (tensor.ElementCount > int.MaxValue)
                throw new InvalidOperationException(
                    "A model tensor exceeds the supported CPU array size.");

            var values =
                new float[(int)tensor.ElementCount];

            initializer.Fill(
                values,
                tensor.Name,
                tensor.Role,
                masterSeed);

            weights.Add(
                tensor.Name,
                values);
        }

        string digest =
            ComputeWeightsSha256(
                layout,
                weights);

        return new FullWorthInitializedModelState(
            layout,
            masterSeed,
            weights,
            digest);
    }

    public ReadOnlyMemory<float> GetRequiredTensor(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _weights.TryGetValue(
            name,
            out float[]? weights)
            ? weights
            : throw new KeyNotFoundException(
                "The requested initialized tensor does not exist.");
    }

    public FullWorthModelCheckpointManifest CreateInitialCheckpointManifest(
        DateTimeOffset createdAtUtc)
    {
        return FullWorthModelCheckpointManifest.CreateInitial(
            Layout.Architecture,
            MasterSeed,
            WeightsSha256,
            createdAtUtc);
    }

    private static string ComputeWeightsSha256(
        FullWorthModelTensorLayout layout,
        IReadOnlyDictionary<string, float[]> weights)
    {
        using IncrementalHash hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        AppendString(
            hash,
            layout.Architecture.CompatibilityId);

        foreach (FullWorthModelTensorSpec tensor in layout.Tensors)
        {
            AppendString(
                hash,
                tensor.Name);

            AppendInt32(
                hash,
                (int)tensor.Role);

            AppendInt32(
                hash,
                tensor.Rows);

            AppendInt32(
                hash,
                tensor.Columns);

            float[] values =
                weights[tensor.Name];

            AppendFloats(
                hash,
                values);
        }

        byte[] digest =
            hash.GetHashAndReset();

        return Convert
            .ToHexString(digest)
            .ToLowerInvariant();
    }

    private static void AppendString(
        IncrementalHash hash,
        string value)
    {
        byte[] bytes =
            Encoding.UTF8.GetBytes(value);

        AppendInt32(
            hash,
            bytes.Length);

        hash.AppendData(bytes);
    }

    private static void AppendInt32(
        IncrementalHash hash,
        int value)
    {
        Span<byte> bytes =
            stackalloc byte[sizeof(int)];

        BinaryPrimitives.WriteInt32LittleEndian(
            bytes,
            value);

        hash.AppendData(bytes);
    }

    private static void AppendFloats(
        IncrementalHash hash,
        float[] values)
    {
        if (BitConverter.IsLittleEndian)
        {
            hash.AppendData(
                MemoryMarshal.AsBytes(
                    values.AsSpan()));

            return;
        }

        Span<byte> bytes =
            stackalloc byte[sizeof(int)];

        foreach (float value in values)
        {
            BinaryPrimitives.WriteInt32LittleEndian(
                bytes,
                BitConverter.SingleToInt32Bits(value));

            hash.AppendData(bytes);
        }
    }
}

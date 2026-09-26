using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FullWorth.API.Services.Intelligence;

public enum FullWorthModelTensorRole
{
    TokenEmbedding = 0,
    AttentionInputProjection = 1,
    AttentionOutputProjection = 2,
    FeedForwardInputProjection = 3,
    FeedForwardOutputProjection = 4,
    NormalizationScale = 5
}

/// <summary>
/// Versioned, reproducible random initialization for FullWorth-owned model
/// weights. Tensor seeds are derived independently from the architecture,
/// tensor name, role, and master seed so traversal order cannot change weights.
/// </summary>
public sealed class FullWorthModelInitializer
{
    public const string Version = "fullworth-random-init-v1";
    public const double BaseStandardDeviation = 0.02d;

    private readonly FullWorthLanguageModelArchitecture _architecture;

    public FullWorthModelInitializer(
        FullWorthLanguageModelArchitecture architecture)
    {
        _architecture =
            architecture ??
            throw new ArgumentNullException(nameof(architecture));
    }

    public void Fill(
        Span<float> destination,
        string tensorName,
        FullWorthModelTensorRole role,
        long masterSeed)
    {
        if (destination.IsEmpty)
            throw new ArgumentException(
                "A trainable tensor must contain at least one value.",
                nameof(destination));

        if (string.IsNullOrWhiteSpace(tensorName))
            throw new ArgumentException(
                "A stable tensor name is required.",
                nameof(tensorName));

        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        if (role == FullWorthModelTensorRole.NormalizationScale)
        {
            destination.Fill(1f);
            return;
        }

        double standardDeviation =
            GetStandardDeviation(role);

        ulong tensorSeed =
            DeriveTensorSeed(
                tensorName,
                role,
                masterSeed);

        var random =
            new SplitMix64(tensorSeed);

        for (int index = 0;
             index < destination.Length;
             index++)
        {
            destination[index] =
                (float)(
                    NextApproximateStandardNormal(ref random) *
                    standardDeviation);
        }
    }

    public double GetStandardDeviation(
        FullWorthModelTensorRole role)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));

        return role switch
        {
            FullWorthModelTensorRole.NormalizationScale =>
                0d,

            FullWorthModelTensorRole.AttentionOutputProjection or
            FullWorthModelTensorRole.FeedForwardOutputProjection =>
                BaseStandardDeviation /
                Math.Sqrt(2d * _architecture.LayerCount),

            _ =>
                BaseStandardDeviation
        };
    }

    private ulong DeriveTensorSeed(
        string tensorName,
        FullWorthModelTensorRole role,
        long masterSeed)
    {
        string descriptor =
            string.Join(
                "|",
                Version,
                _architecture.CompatibilityId,
                masterSeed.ToString(CultureInfo.InvariantCulture),
                ((int)role).ToString(CultureInfo.InvariantCulture),
                tensorName);

        byte[] digest =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(descriptor));

        ulong seed =
            BinaryPrimitives.ReadUInt64LittleEndian(
                digest.AsSpan(0, sizeof(ulong)));

        return seed == 0
            ? 0x9E3779B97F4A7C15UL
            : seed;
    }

    private static double NextApproximateStandardNormal(
        ref SplitMix64 random)
    {
        // Irwin-Hall approximation: sum of 12 U(0,1) values minus 6 has
        // mean 0 and variance 1. It avoids platform-sensitive transcendental
        // functions while remaining suitable for deterministic initialization.
        double total = 0d;

        for (int index = 0; index < 12; index++)
            total += random.NextUnitDouble();

        return total - 6d;
    }

    private struct SplitMix64
    {
        private ulong _state;

        public SplitMix64(ulong seed)
        {
            _state = seed;
        }

        public double NextUnitDouble()
        {
            ulong value = NextUInt64();

            return
                (value >> 11) *
                (1d / 9_007_199_254_740_992d);
        }

        private ulong NextUInt64()
        {
            _state +=
                0x9E3779B97F4A7C15UL;

            ulong value = _state;

            value =
                (value ^ (value >> 30)) *
                0xBF58476D1CE4E5B9UL;

            value =
                (value ^ (value >> 27)) *
                0x94D049BB133111EBUL;

            return value ^ (value >> 31);
        }
    }
}

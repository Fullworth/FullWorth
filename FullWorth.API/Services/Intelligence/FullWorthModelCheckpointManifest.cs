namespace FullWorth.API.Services.Intelligence;

/// <summary>
/// Metadata-only checkpoint provenance. It contains no user evidence, prompts,
/// credentials, raw financial data, or filesystem paths.
/// </summary>
public sealed record FullWorthModelCheckpointManifest(
    string FormatVersion,
    string ArchitectureVersion,
    string ArchitectureCompatibilityId,
    string TokenizerVersion,
    string InitializationVersion,
    long MasterSeed,
    long ParameterCount,
    long TrainingStep,
    long SeenTokenCount,
    DateTimeOffset CreatedAtUtc,
    string WeightsSha256,
    string? OptimizerStateSha256)
{
    public const string CurrentFormatVersion =
        "fullworth-checkpoint-manifest-v1";

    public static FullWorthModelCheckpointManifest CreateInitial(
        FullWorthLanguageModelArchitecture architecture,
        long masterSeed,
        string weightsSha256,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(architecture);

        var manifest =
            new FullWorthModelCheckpointManifest(
                FormatVersion:
                    CurrentFormatVersion,
                ArchitectureVersion:
                    architecture.Version,
                ArchitectureCompatibilityId:
                    architecture.CompatibilityId,
                TokenizerVersion:
                    architecture.TokenizerVersion,
                InitializationVersion:
                    FullWorthModelInitializer.Version,
                MasterSeed:
                    masterSeed,
                ParameterCount:
                    architecture.EstimateParameterCount(),
                TrainingStep:
                    0,
                SeenTokenCount:
                    0,
                CreatedAtUtc:
                    createdAtUtc.ToUniversalTime(),
                WeightsSha256:
                    weightsSha256,
                OptimizerStateSha256:
                    null);

        manifest.ValidateFor(architecture);
        return manifest;
    }

    public void ValidateFor(
        FullWorthLanguageModelArchitecture architecture)
    {
        ArgumentNullException.ThrowIfNull(architecture);

        if (!string.Equals(
                FormatVersion,
                CurrentFormatVersion,
                StringComparison.Ordinal))
            throw InvalidManifest(
                "Checkpoint manifest format is not supported.");

        if (!string.Equals(
                ArchitectureVersion,
                architecture.Version,
                StringComparison.Ordinal) ||
            !string.Equals(
                ArchitectureCompatibilityId,
                architecture.CompatibilityId,
                StringComparison.Ordinal))
            throw InvalidManifest(
                "Checkpoint architecture does not match the runtime architecture.");

        if (!string.Equals(
                TokenizerVersion,
                architecture.TokenizerVersion,
                StringComparison.Ordinal))
            throw InvalidManifest(
                "Checkpoint tokenizer does not match the runtime tokenizer.");

        if (!string.Equals(
                InitializationVersion,
                FullWorthModelInitializer.Version,
                StringComparison.Ordinal))
            throw InvalidManifest(
                "Checkpoint initialization scheme is not supported.");

        if (ParameterCount != architecture.EstimateParameterCount())
            throw InvalidManifest(
                "Checkpoint parameter count does not match the runtime architecture.");

        if (TrainingStep < 0)
            throw InvalidManifest(
                "Checkpoint training step cannot be negative.");

        if (SeenTokenCount < 0)
            throw InvalidManifest(
                "Checkpoint token count cannot be negative.");

        if (CreatedAtUtc.Offset != TimeSpan.Zero)
            throw InvalidManifest(
                "Checkpoint timestamp must be normalized to UTC.");

        ValidateSha256(
            WeightsSha256,
            nameof(WeightsSha256));

        if (TrainingStep == 0)
        {
            if (SeenTokenCount != 0)
                throw InvalidManifest(
                    "An untrained checkpoint cannot report consumed tokens.");

            if (OptimizerStateSha256 is not null)
                throw InvalidManifest(
                    "An initial checkpoint must not contain optimizer state.");
        }
        else
        {
            if (SeenTokenCount == 0)
                throw InvalidManifest(
                    "A trained checkpoint must report consumed tokens.");

            if (OptimizerStateSha256 is null)
                throw InvalidManifest(
                    "A resumable trained checkpoint requires optimizer state.");

            ValidateSha256(
                OptimizerStateSha256,
                nameof(OptimizerStateSha256));
        }
    }

    private static void ValidateSha256(
        string value,
        string fieldName)
    {
        if (value.Length != 64)
            throw InvalidManifest(
                $"{fieldName} must be a lowercase SHA-256 digest.");

        foreach (char character in value)
        {
            bool digit =
                character is >= '0' and <= '9';

            bool lowerHex =
                character is >= 'a' and <= 'f';

            if (!digit && !lowerHex)
                throw InvalidManifest(
                    $"{fieldName} must be a lowercase SHA-256 digest.");
        }
    }

    private static InvalidDataException InvalidManifest(
        string message) =>
        new(message);
}

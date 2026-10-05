using System.Globalization;

public sealed class OcrImageProcessCgroup : IDisposable
{
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string CpuMaxPath = "cpu.max";
    private const string MemoryMaxPath = "memory.max";
    private const string MemorySwapMaxPath = "memory.swap.max";
    private const string OomGroupPath = "memory.oom.group";
    private const string CgroupProcessesPath = "cgroup.procs";
    private const string CgroupControllersPath = "cgroup.controllers";
    private const string CgroupSubtreeControlPath = "cgroup.subtree_control";
    private const string CurrentCgroupFile = "/proc/self/cgroup";
    private const string ImageCpuQuotaVariable = "FULLWORTH_PARSER_IMAGE_CPU_QUOTA_US";
    private const string ImageCpuPeriodVariable = "FULLWORTH_PARSER_IMAGE_CPU_PERIOD_US";
    private const string ImageMemoryLimitVariable = "FULLWORTH_PARSER_IMAGE_MEMORY_MAX_BYTES";
    private const string ImageSwapLimitVariable = "FULLWORTH_PARSER_IMAGE_SWAP_MAX_BYTES";
    private const string DocumentCpuQuotaVariable = "FULLWORTH_PARSER_DOCUMENT_CPU_QUOTA_US";
    private const string DocumentCpuPeriodVariable = "FULLWORTH_PARSER_DOCUMENT_CPU_PERIOD_US";
    private const string DocumentMemoryLimitVariable = "FULLWORTH_PARSER_DOCUMENT_MEMORY_MAX_BYTES";
    private const string DocumentSwapLimitVariable = "FULLWORTH_PARSER_DOCUMENT_SWAP_MAX_BYTES";
    private const string ObservationDelayVariable = "FULLWORTH_PARSER_IMAGE_OBSERVATION_DELAY_MS";
    private const int MaximumObservationDelayMilliseconds = 1_000;

    private readonly string _path;
    private bool _disposed;

    private static readonly string[] ConfigurationVariables =
    {
        ImageCpuQuotaVariable,
        ImageCpuPeriodVariable,
        ImageMemoryLimitVariable,
        ImageSwapLimitVariable,
        DocumentCpuQuotaVariable,
        DocumentCpuPeriodVariable,
        DocumentMemoryLimitVariable,
        DocumentSwapLimitVariable,
        ObservationDelayVariable
    };

    private OcrImageProcessCgroup(string path)
    {
        _path = path;
    }

    public static void CopyConfiguredEnvironment(
        IDictionary<string, string?> target,
        Func<string, string?> getEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        foreach (var variable in ConfigurationVariables)
        {
            var value = getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                target[variable] = value;
            }
        }
    }

    public static bool TryAttachProcess(
        int processId,
        out OcrImageProcessCgroup? scope) =>
        TryAttachProcess(processId, out scope, out _);

    public static bool TryAttachProcess(
        int processId,
        out OcrImageProcessCgroup? scope,
        out string failureCode)
    {
        scope = null;
        failureCode = string.Empty;

        if (!OperatingSystem.IsLinux())
        {
            failureCode = "image_cgroup_requires_linux";
            return false;
        }

        if (processId <= 0)
        {
            failureCode = "image_process_id_invalid";
            return false;
        }

        if (!TryReadCurrentCgroupPath(out var documentPath))
        {
            failureCode = "document_cgroup_unreadable";
            return false;
        }

        if (!IsOcrDocumentCgroup(documentPath))
        {
            failureCode = "document_cgroup_unexpected";
            return false;
        }

        if (!TryReadProcessCgroupPath(processId, out var initialProcessPath))
        {
            failureCode = "image_process_cgroup_unreadable";
            return false;
        }

        if (!string.Equals(
                initialProcessPath,
                documentPath,
                StringComparison.Ordinal))
        {
            failureCode = "image_process_not_in_document_cgroup";
            return false;
        }

        if (!TryGetDelegatedParent(documentPath, out var delegatedParentPath))
        {
            failureCode = "delegated_parent_unavailable";
            return false;
        }

        if (!HasDelegatedControllers(delegatedParentPath))
        {
            failureCode = "delegated_controllers_unavailable";
            return false;
        }

        if (!TryReadConfiguredLimits(out var limits))
        {
            failureCode = "image_limits_invalid";
            return false;
        }

        if (!LimitsFitInsideDocument(documentPath, limits))
        {
            failureCode = "image_limits_exceed_document";
            return false;
        }

        string? imagePath = null;

        try
        {
            /*
             * The image worker is born in the document cgroup, then moved once
             * into a fresh sibling leaf before it receives image bytes. This
             * follows cgroup v2's delegation/no-internal-process model and keeps
             * native model loading, decode, and recognition out of the document
             * process itself.
             */
            imagePath = BuildCgroupPath(
                delegatedParentPath,
                processId,
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(imagePath);
            File.WriteAllText(
                Path.Combine(imagePath, CpuMaxPath),
                $"{limits.CpuQuota} {limits.CpuPeriod}");
            File.WriteAllText(
                Path.Combine(imagePath, MemoryMaxPath),
                limits.MemoryMax.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(
                Path.Combine(imagePath, MemorySwapMaxPath),
                limits.SwapMax.ToString(CultureInfo.InvariantCulture));
            File.WriteAllText(
                Path.Combine(imagePath, OomGroupPath),
                "1");
            File.WriteAllText(
                Path.Combine(imagePath, CgroupProcessesPath),
                processId.ToString(CultureInfo.InvariantCulture));

            if (!TryReadProcessCgroupPath(processId, out var actualPath) ||
                !string.Equals(
                    actualPath,
                    Path.GetFullPath(imagePath),
                    StringComparison.Ordinal))
            {
                failureCode = "image_cgroup_membership_unverified";
                TryRestoreAndDelete(
                    documentPath,
                    imagePath,
                    processId);
                return false;
            }

            scope = new OcrImageProcessCgroup(imagePath);
            return true;
        }
        catch (IOException)
        {
            failureCode = "image_cgroup_io_failure";
            TryRestoreAndDelete(
                documentPath,
                imagePath,
                processId);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            failureCode = "image_cgroup_access_denied";
            TryRestoreAndDelete(
                documentPath,
                imagePath,
                processId);
            return false;
        }
        catch (InvalidOperationException)
        {
            failureCode = "image_cgroup_state_invalid";
            TryRestoreAndDelete(
                documentPath,
                imagePath,
                processId);
            return false;
        }
    }

    public static string BuildCgroupPath(
        string delegatedParentPath,
        int processId,
        string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(delegatedParentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        if (processId <= 0 ||
            instanceId.IndexOfAny(new[] { '/', '\\' }) >= 0 ||
            instanceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        var parentFullPath = Path.GetFullPath(delegatedParentPath);
        var childFullPath = Path.GetFullPath(
            Path.Combine(
                parentFullPath,
                $"fullworth-ocr-image-{processId.ToString(CultureInfo.InvariantCulture)}-{instanceId}"));

        if (!childFullPath.StartsWith(
                parentFullPath + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "OCR image cgroup escaped its delegated parent.");
        }

        return childFullPath;
    }

    public static TimeSpan GetConfiguredObservationDelay()
    {
        var configured = Environment.GetEnvironmentVariable(
            ObservationDelayVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            return TimeSpan.Zero;
        }

        if (!int.TryParse(
                configured,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var milliseconds) ||
            milliseconds < 0 ||
            milliseconds > MaximumObservationDelayMilliseconds)
        {
            throw new InvalidOperationException(
                "The OCR image observation delay is outside its safe range.");
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public static bool HasValidLimitConfiguration(
        string cpuQuota,
        string cpuPeriod,
        string memoryMax,
        string swapMax)
    {
        return TryParsePositive(cpuQuota, out _) &&
               TryParsePositive(cpuPeriod, out _) &&
               TryParsePositive(memoryMax, out _) &&
               TryParseNonNegative(swapMax, out _);
    }

    public static bool LimitsFitInsideParent(
        string imageCpu,
        string parentCpu,
        string imageMemory,
        string parentMemory,
        string imageSwap,
        string parentSwap)
    {
        return TryParseCpu(imageCpu, out var imageQuota, out var imagePeriod) &&
               TryParseCpu(parentCpu, out var parentQuota, out var parentPeriod) &&
               imagePeriod == parentPeriod &&
               imageQuota <= parentQuota &&
               TryParsePositive(imageMemory, out var imageMemoryBytes) &&
               TryParsePositive(parentMemory, out var parentMemoryBytes) &&
               imageMemoryBytes <= parentMemoryBytes &&
               TryParseNonNegative(imageSwap, out var imageSwapBytes) &&
               TryParseNonNegative(parentSwap, out var parentSwapBytes) &&
               imageSwapBytes <= parentSwapBytes;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        TryDeleteCgroup(_path);
    }

    private static bool TryReadConfiguredLimits(out ImageLimits limits)
    {
        var cpuQuota = Environment.GetEnvironmentVariable(ImageCpuQuotaVariable)
            ?? Environment.GetEnvironmentVariable(DocumentCpuQuotaVariable);
        var cpuPeriod = Environment.GetEnvironmentVariable(ImageCpuPeriodVariable)
            ?? Environment.GetEnvironmentVariable(DocumentCpuPeriodVariable);
        var memoryMax = Environment.GetEnvironmentVariable(ImageMemoryLimitVariable)
            ?? Environment.GetEnvironmentVariable(DocumentMemoryLimitVariable);
        var swapMax = Environment.GetEnvironmentVariable(ImageSwapLimitVariable)
            ?? Environment.GetEnvironmentVariable(DocumentSwapLimitVariable);

        if (!HasValidLimitConfiguration(
                cpuQuota ?? string.Empty,
                cpuPeriod ?? string.Empty,
                memoryMax ?? string.Empty,
                swapMax ?? string.Empty) ||
            !long.TryParse(cpuQuota, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCpuQuota) ||
            !long.TryParse(cpuPeriod, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCpuPeriod) ||
            !long.TryParse(memoryMax, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMemoryMax) ||
            !long.TryParse(swapMax, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSwapMax))
        {
            limits = default;
            return false;
        }

        limits = new ImageLimits(
            parsedCpuQuota,
            parsedCpuPeriod,
            parsedMemoryMax,
            parsedSwapMax);
        return true;
    }

    private static bool LimitsFitInsideDocument(
        string documentPath,
        ImageLimits limits)
    {
        try
        {
            return LimitsFitInsideParent(
                $"{limits.CpuQuota} {limits.CpuPeriod}",
                File.ReadAllText(Path.Combine(documentPath, CpuMaxPath)),
                limits.MemoryMax.ToString(CultureInfo.InvariantCulture),
                File.ReadAllText(Path.Combine(documentPath, MemoryMaxPath)),
                limits.SwapMax.ToString(CultureInfo.InvariantCulture),
                File.ReadAllText(Path.Combine(documentPath, MemorySwapMaxPath)));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryGetDelegatedParent(
        string documentPath,
        out string parentPath)
    {
        parentPath = string.Empty;

        var documentFullPath = Path.GetFullPath(documentPath);
        var root = Path.GetFullPath(CgroupRoot);
        if (!documentFullPath.StartsWith(
                root + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            return false;
        }

        var parent = Directory.GetParent(documentFullPath);
        if (parent is null ||
            parent.FullName.Equals(root, StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(parent.FullName, CgroupControllersPath)) ||
            !File.Exists(Path.Combine(parent.FullName, CgroupSubtreeControlPath)) ||
            !File.Exists(Path.Combine(parent.FullName, CgroupProcessesPath)))
        {
            return false;
        }

        parentPath = parent.FullName;
        return true;
    }

    private static bool HasDelegatedControllers(string parentPath)
    {
        try
        {
            var available = File.ReadAllText(
                Path.Combine(parentPath, CgroupControllersPath));
            var enabled = File.ReadAllText(
                Path.Combine(parentPath, CgroupSubtreeControlPath));

            return ContainsController(available, "cpu") &&
                   ContainsController(available, "memory") &&
                   ContainsController(enabled, "cpu") &&
                   ContainsController(enabled, "memory");
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsOcrDocumentCgroup(string path)
    {
        var name = Path.GetFileName(path);
        if (!name.StartsWith(
                "fullworth-ocr-",
                StringComparison.Ordinal))
        {
            return false;
        }

        var suffix = name["fullworth-ocr-".Length..];
        return suffix.Length > 0 &&
               char.IsAsciiDigit(suffix[0]);
    }

    private static bool TryReadCurrentCgroupPath(out string path) =>
        TryReadCgroupPath(
            CurrentCgroupFile,
            out path);

    private static bool TryReadProcessCgroupPath(
        int processId,
        out string path) =>
        TryReadCgroupPath(
            $"/proc/{processId.ToString(CultureInfo.InvariantCulture)}/cgroup",
            out path);

    private static bool TryReadCgroupPath(
        string cgroupFile,
        out string path)
    {
        path = string.Empty;

        try
        {
            foreach (var line in File.ReadLines(cgroupFile))
            {
                if (!line.StartsWith("0::", StringComparison.Ordinal))
                {
                    continue;
                }

                var relativePath = line[3..].Trim();
                if (relativePath.Length == 0 ||
                    relativePath.Contains("..", StringComparison.Ordinal))
                {
                    return false;
                }

                var candidate = Path.GetFullPath(
                    Path.Combine(
                        CgroupRoot,
                        relativePath.TrimStart('/', '\\')));
                var root = Path.GetFullPath(CgroupRoot);
                if (!candidate.StartsWith(
                        root + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal))
                {
                    return false;
                }

                path = candidate;
                return true;
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static bool ContainsController(string value, string controller) =>
        value.Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(controller, StringComparer.Ordinal);

    private static bool TryParseCpu(
        string value,
        out long quota,
        out long period)
    {
        quota = 0;
        period = 0;
        var parts = value.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 &&
               TryParsePositive(parts[0], out quota) &&
               TryParsePositive(parts[1], out period);
    }

    private static bool TryParsePositive(string value, out long parsed) =>
        long.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed) &&
        parsed > 0;

    private static bool TryParseNonNegative(string value, out long parsed) =>
        long.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed) &&
        parsed >= 0;

    private static void TryRestoreAndDelete(
        string documentPath,
        string? imagePath,
        int processId)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        try
        {
            if (TryReadProcessCgroupPath(processId, out var currentPath) &&
                string.Equals(currentPath, imagePath, StringComparison.Ordinal))
            {
                File.WriteAllText(
                    Path.Combine(documentPath, CgroupProcessesPath),
                    processId.ToString(CultureInfo.InvariantCulture));
            }

            TryDeleteCgroup(imagePath);
        }
        catch
        {
            // The caller kills the untrusted image worker on attach failure.
        }
    }

    private static void TryDeleteCgroup(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path);
        }
        catch
        {
            // Cleanup is verified by the production container/deploy gates.
        }
    }

    private readonly record struct ImageLimits(
        long CpuQuota,
        long CpuPeriod,
        long MemoryMax,
        long SwapMax);
}

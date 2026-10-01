using System.Globalization;

public static class WorkerResourceLimits
{
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string CpuMaxPath = "cpu.max";
    private const string MemoryMaxPath = "memory.max";
    private const string MemorySwapMaxPath = "memory.swap.max";
    private const string OomGroupPath = "memory.oom.group";
    private const string CgroupProcessesPath = "cgroup.procs";
    private const string CurrentCgroupFile = "/proc/self/cgroup";
    private const string CpuQuotaVariable = "FULLWORTH_PARSER_WORKER_CPU_QUOTA_US";
    private const string CpuPeriodVariable = "FULLWORTH_PARSER_WORKER_CPU_PERIOD_US";
    private const string MemoryLimitVariable = "FULLWORTH_PARSER_WORKER_MEMORY_MAX_BYTES";
    private const string SwapLimitVariable = "FULLWORTH_PARSER_WORKER_SWAP_MAX_BYTES";

    private static readonly object Sync = new();
    private static string? _parentCgroupPath;
    private static string? _workerCgroupPath;
    private static bool _limitsApplied;
    private static bool _cleanupRegistered;

    public static bool TryApplyCurrentProcessLimits()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        lock (Sync)
        {
            if (_limitsApplied)
            {
                return true;
            }

            if (!TryReadConfiguredLimits(out var limits) ||
                !File.Exists(Path.Combine(CgroupRoot, "cgroup.controllers")))
            {
                return false;
            }

            string? childPath = null;
            try
            {
                var parentPath = GetCurrentCgroupPath();
                if (parentPath is null || !Directory.Exists(parentPath))
                {
                    return false;
                }

                childPath = BuildWorkerCgroupPath(parentPath, Environment.ProcessId, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(childPath);

                File.WriteAllText(Path.Combine(childPath, CpuMaxPath), $"{limits.CpuQuota} {limits.CpuPeriod}");
                File.WriteAllText(Path.Combine(childPath, MemoryMaxPath), limits.MemoryMax.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(childPath, MemorySwapMaxPath), limits.SwapMax.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(Path.Combine(childPath, OomGroupPath), "1");
                File.WriteAllText(
                    Path.Combine(childPath, CgroupProcessesPath),
                    Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

                _parentCgroupPath = parentPath;
                _workerCgroupPath = childPath;
                _limitsApplied = true;
                if (!_cleanupRegistered)
                {
                    AppDomain.CurrentDomain.ProcessExit += static (_, _) => CleanupWorkerCgroup();
                    _cleanupRegistered = true;
                }

                return true;
            }
            catch (IOException)
            {
                DeleteCgroup(childPath);
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                DeleteCgroup(childPath);
                return false;
            }
            catch (InvalidOperationException)
            {
                DeleteCgroup(childPath);
                return false;
            }
        }
    }

    public static bool AreCurrentProcessLimitsEnforced()
    {
        lock (Sync)
        {
            if (!_limitsApplied || string.IsNullOrWhiteSpace(_workerCgroupPath))
            {
                return false;
            }

            try
            {
                var currentPath = GetCurrentCgroupPath();
                return string.Equals(currentPath, _workerCgroupPath, StringComparison.Ordinal) &&
                       HasFiniteCpuLimit(File.ReadAllText(Path.Combine(_workerCgroupPath, CpuMaxPath))) &&
                       HasFiniteMemoryLimit(File.ReadAllText(Path.Combine(_workerCgroupPath, MemoryMaxPath))) &&
                       HasFiniteSwapLimit(File.ReadAllText(Path.Combine(_workerCgroupPath, MemorySwapMaxPath)));
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
    }

    public static string BuildWorkerCgroupPath(string parentPath, int processId, string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        if (processId <= 0 || instanceId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        return Path.GetFullPath(Path.Combine(
            parentPath,
            $"fullworth-parser-worker-{processId.ToString(CultureInfo.InvariantCulture)}-{instanceId}"));
    }

    public static bool AreCurrentContainerLimitsEnforced()
    {
        try
        {
            return HasFiniteCpuLimit(File.ReadAllText(Path.Combine(CgroupRoot, CpuMaxPath))) &&
                   HasFiniteMemoryLimit(File.ReadAllText(Path.Combine(CgroupRoot, MemoryMaxPath))) &&
                   HasFiniteSwapLimit(File.ReadAllText(Path.Combine(CgroupRoot, MemorySwapMaxPath)));
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

    public static bool HasValidWorkerLimitConfiguration(
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

    public static bool HasFiniteMemoryLimit(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParsePositive(value, out _);
    }

    public static bool HasFiniteSwapLimit(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParseNonNegative(value, out _);
    }

    public static bool HasFiniteCpuLimit(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split(
            new[] { ' ', '\t', '\r', '\n' },
            StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 2 &&
               !parts[0].Equals("max", StringComparison.OrdinalIgnoreCase) &&
               TryParsePositive(parts[0], out _) &&
               TryParsePositive(parts[1], out _);
    }

    private static bool TryReadConfiguredLimits(out WorkerLimits limits)
    {
        var cpuQuota = Environment.GetEnvironmentVariable(CpuQuotaVariable);
        var cpuPeriod = Environment.GetEnvironmentVariable(CpuPeriodVariable);
        var memoryMax = Environment.GetEnvironmentVariable(MemoryLimitVariable);
        var swapMax = Environment.GetEnvironmentVariable(SwapLimitVariable);

        if (!HasValidWorkerLimitConfiguration(cpuQuota ?? string.Empty, cpuPeriod ?? string.Empty, memoryMax ?? string.Empty, swapMax ?? string.Empty) ||
            !long.TryParse(cpuQuota, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCpuQuota) ||
            !long.TryParse(cpuPeriod, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedCpuPeriod) ||
            !long.TryParse(memoryMax, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMemoryMax) ||
            !long.TryParse(swapMax, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSwapMax))
        {
            limits = default;
            return false;
        }

        limits = new WorkerLimits(parsedCpuQuota, parsedCpuPeriod, parsedMemoryMax, parsedSwapMax);
        return true;
    }

    private static string? GetCurrentCgroupPath()
    {
        foreach (var line in File.ReadLines(CurrentCgroupFile))
        {
            if (!line.StartsWith("0::", StringComparison.Ordinal))
            {
                continue;
            }

            var relativePath = line[3..].Trim();
            if (relativePath.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var candidate = Path.GetFullPath(Path.Combine(CgroupRoot, relativePath.TrimStart('/', '\\')));
            var root = Path.GetFullPath(CgroupRoot);
            return candidate.Equals(root, StringComparison.Ordinal) ||
                   candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                ? candidate
                : null;
        }

        return null;
    }

    private static void CleanupWorkerCgroup()
    {
        lock (Sync)
        {
            if (!_limitsApplied || _workerCgroupPath is null || _parentCgroupPath is null)
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    Path.Combine(_parentCgroupPath, CgroupProcessesPath),
                    Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }
            catch
            {
                return;
            }

            DeleteCgroup(_workerCgroupPath);
            _limitsApplied = false;
        }
    }

    private static void DeleteCgroup(string? path)
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
            // Cleanup is best effort during startup failure or process shutdown.
        }
    }

    private static bool TryParsePositive(string value, out long parsed)
    {
        return TryParse(value, out parsed) && parsed > 0;
    }

    private static bool TryParseNonNegative(string value, out long parsed)
    {
        return TryParse(value, out parsed) && parsed >= 0;
    }

    private static bool TryParse(string value, out long parsed)
    {
        return long.TryParse(
            value.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private readonly record struct WorkerLimits(long CpuQuota, long CpuPeriod, long MemoryMax, long SwapMax);
}

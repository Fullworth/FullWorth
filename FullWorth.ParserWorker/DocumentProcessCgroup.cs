using System.Globalization;

public sealed class DocumentProcessCgroup : IDisposable
{
    private const string CgroupRoot = "/sys/fs/cgroup";
    private const string SupervisorCgroupName = "fullworth-supervisor";
    private const string CpuMaxPath = "cpu.max";
    private const string MemoryMaxPath = "memory.max";
    private const string MemorySwapMaxPath = "memory.swap.max";
    private const string MemoryEventsPath = "memory.events";
    private const string MemoryPeakPath = "memory.peak";
    private const string PidsMaxPath = "pids.max";
    private const string OomGroupPath = "memory.oom.group";
    private const string CgroupProcessesPath = "cgroup.procs";
    private const string CgroupControllersPath = "cgroup.controllers";
    private const string CgroupSubtreeControlPath = "cgroup.subtree_control";
    private const string CurrentCgroupFile = "/proc/self/cgroup";
    private const string CpuQuotaVariable = "FULLWORTH_PARSER_DOCUMENT_CPU_QUOTA_US";
    private const string CpuPeriodVariable = "FULLWORTH_PARSER_DOCUMENT_CPU_PERIOD_US";
    private const string MemoryLimitVariable = "FULLWORTH_PARSER_DOCUMENT_MEMORY_MAX_BYTES";
    private const string SwapLimitVariable = "FULLWORTH_PARSER_DOCUMENT_SWAP_MAX_BYTES";
    private const string PidsLimitVariable = "FULLWORTH_PARSER_DOCUMENT_PIDS_MAX";

    private readonly string _path;
    private bool _disposed;

    private DocumentProcessCgroup(string path)
    {
        _path = path;
    }

    public string CgroupPath => _path;

    public bool TryReadMemoryEvidence(
        out long memoryMaxBytes,
        out long memoryPeakBytes,
        out long oomKillCount)
    {
        memoryMaxBytes = 0;
        memoryPeakBytes = 0;
        oomKillCount = 0;

        if (_disposed)
        {
            return false;
        }

        try
        {
            if (!TryParsePositive(
                    File.ReadAllText(
                        Path.Combine(
                            _path,
                            MemoryMaxPath)),
                    out memoryMaxBytes) ||
                !TryParseNonNegative(
                    File.ReadAllText(
                        Path.Combine(
                            _path,
                            MemoryPeakPath)),
                    out memoryPeakBytes))
            {
                return false;
            }

            var foundOomKill =
                false;

            foreach (var line in
                     File.ReadLines(
                         Path.Combine(
                             _path,
                             MemoryEventsPath)))
            {
                var parts =
                    line.Split(
                        new[]
                        {
                            ' ',
                            '\t'
                        },
                        StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length !=
                        2 ||
                    !string.Equals(
                        parts[0],
                        "oom_kill",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!TryParseNonNegative(
                        parts[1],
                        out oomKillCount))
                {
                    return false;
                }

                foundOomKill =
                    true;

                break;
            }

            return foundOomKill;
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

    public static bool IsDelegationReady()
    {
        if (!OperatingSystem.IsLinux() ||
            !TryReadConfiguredLimits(out var limits) ||
            !TryGetDelegatedParent(out var parentPath))
        {
            return false;
        }

        try
        {
            var subtreeControl =
                File.ReadAllText(
                    Path.Combine(
                        parentPath,
                        CgroupSubtreeControlPath));

            if (!ContainsController(subtreeControl, "cpu") ||
                !ContainsController(subtreeControl, "memory") ||
                !ContainsController(subtreeControl, "pids") ||
                !WorkerResourceLimits.HasFiniteCpuLimit(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            CpuMaxPath))) ||
                !WorkerResourceLimits.HasFiniteMemoryLimit(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            MemoryMaxPath))) ||
                !WorkerResourceLimits.HasFiniteSwapLimit(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            MemorySwapMaxPath))) ||
                !HasFinitePidsLimit(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            PidsMaxPath))))
            {
                return false;
            }

            return LimitsFitInsideParent(
                parentPath,
                limits);
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

    public static bool TryAttachProcess(
        int processId,
        string workload,
        out DocumentProcessCgroup? cgroup)
    {
        cgroup = null;

        if (!OperatingSystem.IsLinux() ||
            processId <= 0 ||
            !IsSafeWorkloadName(workload) ||
            !TryReadConfiguredLimits(out var limits) ||
            !TryGetDelegatedParent(out var parentPath) ||
            !LimitsFitInsideParent(parentPath, limits))
        {
            return false;
        }

        string? childPath = null;

        try
        {
            childPath =
                BuildDocumentCgroupPath(
                    parentPath,
                    workload,
                    processId,
                    Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(
                childPath);

            File.WriteAllText(
                Path.Combine(
                    childPath,
                    CpuMaxPath),
                $"{limits.CpuQuota} {limits.CpuPeriod}");

            File.WriteAllText(
                Path.Combine(
                    childPath,
                    MemoryMaxPath),
                limits.MemoryMax.ToString(
                    CultureInfo.InvariantCulture));

            File.WriteAllText(
                Path.Combine(
                    childPath,
                    MemorySwapMaxPath),
                limits.SwapMax.ToString(
                    CultureInfo.InvariantCulture));

            File.WriteAllText(
                Path.Combine(
                    childPath,
                    PidsMaxPath),
                limits.PidsMax.ToString(
                    CultureInfo.InvariantCulture));

            File.WriteAllText(
                Path.Combine(
                    childPath,
                    OomGroupPath),
                "1");

            File.WriteAllText(
                Path.Combine(
                    childPath,
                    CgroupProcessesPath),
                processId.ToString(
                    CultureInfo.InvariantCulture));

            var expectedRelativePath =
                "/" +
                Path.GetRelativePath(
                        CgroupRoot,
                        childPath)
                    .Replace(
                        Path.DirectorySeparatorChar,
                        '/');

            var actualRelativePath =
                ReadUnifiedCgroupRelativePath(
                    $"/proc/{processId}/cgroup");

            if (!string.Equals(
                    actualRelativePath,
                    expectedRelativePath,
                    StringComparison.Ordinal))
            {
                TryDeleteCgroup(
                    childPath);

                return false;
            }

            cgroup =
                new DocumentProcessCgroup(
                    childPath);

            return true;
        }
        catch (IOException)
        {
            TryDeleteCgroup(
                childPath);

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            TryDeleteCgroup(
                childPath);

            return false;
        }
        catch (InvalidOperationException)
        {
            TryDeleteCgroup(
                childPath);

            return false;
        }
    }

    public static string BuildDocumentCgroupPath(
        string parentPath,
        string workload,
        int processId,
        string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            parentPath);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            workload);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            instanceId);

        if (processId <= 0 ||
            !IsSafeWorkloadName(
                workload) ||
            instanceId.IndexOfAny(
                Path.GetInvalidFileNameChars()) >=
                0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processId));
        }

        var parentFullPath =
            Path.GetFullPath(
                parentPath);

        var childFullPath =
            Path.GetFullPath(
                Path.Combine(
                    parentFullPath,
                    $"fullworth-{workload}-{processId.ToString(CultureInfo.InvariantCulture)}-{instanceId}"));

        if (!childFullPath.StartsWith(
                parentFullPath +
                Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Document cgroup escaped its delegated parent.");
        }

        return childFullPath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        TryDeleteCgroup(
            _path);
    }

    private static bool TryGetDelegatedParent(
        out string parentPath)
    {
        parentPath =
            string.Empty;

        var currentRelativePath =
            ReadUnifiedCgroupRelativePath(
                CurrentCgroupFile);

        if (string.IsNullOrWhiteSpace(
                currentRelativePath))
        {
            return false;
        }

        var currentPath =
            Path.GetFullPath(
                Path.Combine(
                    CgroupRoot,
                    currentRelativePath.TrimStart(
                        '/',
                        '\\')));

        var cgroupRoot =
            Path.GetFullPath(
                CgroupRoot);

        if (!currentPath.StartsWith(
                cgroupRoot +
                Path.DirectorySeparatorChar,
                StringComparison.Ordinal) ||
            !string.Equals(
                Path.GetFileName(
                    currentPath),
                SupervisorCgroupName,
                StringComparison.Ordinal))
        {
            return false;
        }

        var parent =
            Directory.GetParent(
                currentPath);

        if (parent is null ||
            parent.FullName.Equals(
                cgroupRoot,
                StringComparison.Ordinal) ||
            !File.Exists(
                Path.Combine(
                    parent.FullName,
                    CgroupControllersPath)))
        {
            return false;
        }

        parentPath =
            parent.FullName;

        return true;
    }

    private static bool LimitsFitInsideParent(
        string parentPath,
        DocumentLimits limits)
    {
        try
        {
            if (!TryParsePositive(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            MemoryMaxPath)),
                    out var parentMemory) ||
                limits.MemoryMax >
                    parentMemory ||
                !TryParseNonNegative(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            MemorySwapMaxPath)),
                    out var parentSwap) ||
                limits.SwapMax >
                    parentSwap ||
                !TryParsePositive(
                    File.ReadAllText(
                        Path.Combine(
                            parentPath,
                            PidsMaxPath)),
                    out var parentPids) ||
                limits.PidsMax >
                    parentPids)
            {
                return false;
            }

            return true;
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

    private static bool TryReadConfiguredLimits(
        out DocumentLimits limits)
    {
        var cpuQuota =
            Environment.GetEnvironmentVariable(
                CpuQuotaVariable);

        var cpuPeriod =
            Environment.GetEnvironmentVariable(
                CpuPeriodVariable);

        var memoryMax =
            Environment.GetEnvironmentVariable(
                MemoryLimitVariable);

        var swapMax =
            Environment.GetEnvironmentVariable(
                SwapLimitVariable);

        var pidsMax =
            Environment.GetEnvironmentVariable(
                PidsLimitVariable);

        if (!TryParsePositive(
                cpuQuota,
                out var parsedCpuQuota) ||
            !TryParsePositive(
                cpuPeriod,
                out var parsedCpuPeriod) ||
            !TryParsePositive(
                memoryMax,
                out var parsedMemoryMax) ||
            !TryParseNonNegative(
                swapMax,
                out var parsedSwapMax) ||
            !TryParsePositive(
                pidsMax,
                out var parsedPidsMax))
        {
            limits =
                default;

            return false;
        }

        limits =
            new DocumentLimits(
                parsedCpuQuota,
                parsedCpuPeriod,
                parsedMemoryMax,
                parsedSwapMax,
                parsedPidsMax);

        return true;
    }

    private static string? ReadUnifiedCgroupRelativePath(
        string cgroupFile)
    {
        try
        {
            foreach (var line in
                     File.ReadLines(
                         cgroupFile))
            {
                if (!line.StartsWith(
                        "0::",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var relativePath =
                    line[3..]
                        .Trim();

                if (relativePath.Length ==
                        0 ||
                    relativePath.Contains(
                        "..",
                        StringComparison.Ordinal))
                {
                    return null;
                }

                return relativePath;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static bool ContainsController(
        string value,
        string controller)
    {
        return value
            .Split(
                new[]
                {
                    ' ',
                    '\t',
                    '\r',
                    '\n'
                },
                StringSplitOptions.RemoveEmptyEntries)
            .Contains(
                controller,
                StringComparer.Ordinal);
    }

    private static bool HasFinitePidsLimit(
        string value)
    {
        return !string.IsNullOrWhiteSpace(
                   value) &&
               !value.Trim()
                   .Equals(
                       "max",
                       StringComparison.OrdinalIgnoreCase) &&
               TryParsePositive(
                   value,
                   out _);
    }

    private static bool IsSafeWorkloadName(
        string workload)
    {
        return workload is "ocr" or "pdf";
    }

    private static bool TryParsePositive(
        string? value,
        out long parsed)
    {
        return TryParse(
                   value,
                   out parsed) &&
               parsed >
               0;
    }

    private static bool TryParseNonNegative(
        string? value,
        out long parsed)
    {
        return TryParse(
                   value,
                   out parsed) &&
               parsed >=
               0;
    }

    private static bool TryParse(
        string? value,
        out long parsed)
    {
        return long.TryParse(
            value?.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out parsed);
    }

    private static void TryDeleteCgroup(
        string? path)
    {
        if (string.IsNullOrWhiteSpace(
                path))
        {
            return;
        }

        try
        {
            Directory.Delete(
                path);
        }
        catch
        {
            // Process termination is authoritative. Cleanup is verified separately.
        }
    }

    private readonly record struct DocumentLimits(
        long CpuQuota,
        long CpuPeriod,
        long MemoryMax,
        long SwapMax,
        long PidsMax);
}

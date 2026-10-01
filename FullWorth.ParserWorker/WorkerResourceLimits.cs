using System.Globalization;

public static class WorkerResourceLimits
{
    private const string CpuMaxPath = "/sys/fs/cgroup/cpu.max";
    private const string MemoryMaxPath = "/sys/fs/cgroup/memory.max";

    public static bool AreCurrentProcessLimitsEnforced()
    {
        if (!OperatingSystem.IsLinux())
        {
            return false;
        }

        try
        {
            return HasFiniteCpuLimit(File.ReadAllText(CpuMaxPath)) &&
                   HasFiniteMemoryLimit(File.ReadAllText(MemoryMaxPath));
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

    public static bool HasFiniteMemoryLimit(string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Trim().Equals("max", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return TryParsePositive(value, out _);
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

    private static bool TryParsePositive(string value, out long parsed)
    {
        return long.TryParse(
                   value.Trim(),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out parsed) &&
               parsed > 0;
    }
}

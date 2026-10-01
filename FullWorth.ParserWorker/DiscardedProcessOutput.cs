/// <summary>Drains process diagnostics without retaining their contents.</summary>
public static class DiscardedProcessOutput
{
    public static async Task DrainAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The source stream is not readable.", nameof(source));
        }

        var buffer = new byte[8 * 1024];
        while (await source.ReadAsync(buffer.AsMemory(), cancellationToken) != 0)
        {
        }
    }
}

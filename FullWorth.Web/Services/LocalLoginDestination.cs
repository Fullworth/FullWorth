namespace FullWorth.Web.Services;

// Login destinations are navigation hints, never authorization decisions.
public static class LocalLoginDestination
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048) return "/app";
        // Keep the protected pending-MFA cookie below browser/header bounds,
        // including JSON escaping, encryption and base64 expansion.
        if (System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value).Length > 512) return "/app";
        var decoded = value;
        try
        {
            for (var round = 0; round < 4; round++)
            {
                if (!Safe(decoded)) return "/app";
                var next = Uri.UnescapeDataString(decoded);
                if (next == decoded) return value;
                decoded = next;
            }
        }
        catch (UriFormatException) { }
        return "/app";
    }

    private static bool Safe(string value) =>
        value.StartsWith("/", StringComparison.Ordinal) &&
        !value.StartsWith("//", StringComparison.Ordinal) &&
        !value.Contains('\\') && !value.Any(char.IsControl) &&
        HasValidPercentEscapes(value);

    // Uri.UnescapeDataString keeps invalid '%' escapes verbatim instead of
    // always throwing. Reject them before treating a return hint as a local URL.
    private static bool HasValidPercentEscapes(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '%') continue;
            if (i + 2 >= value.Length ||
                !Uri.IsHexDigit(value[i + 1]) ||
                !Uri.IsHexDigit(value[i + 2])) return false;
            i += 2;
        }
        return true;
    }
}

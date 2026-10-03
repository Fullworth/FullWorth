using System.Text.Json;

public static class WorkerProtocol
{
    public const int MaxRequestBytes = 15 * 1024 * 1024;
    public const int MaxResponseBytes = 1_100_000;

    public static async Task<WorkerResponse> BuildResponseAsync(
        Stream input,
        int maxBytes,
        TimeSpan requestTimeout,
        Func<bool>? resourceLimitsVerified = null)
    {
        var buffer = new byte[4096];
        using var request = new MemoryStream();
        using var timeout = new CancellationTokenSource(requestTimeout);
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), timeout.Token);
                if (read == 0)
                {
                    break;
                }

                if (request.Length + read > maxBytes)
                {
                    return new WorkerResponse(1, "rejected", "request_too_large");
                }

                await request.WriteAsync(buffer.AsMemory(0, read));
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return new WorkerResponse(1, "rejected", "request_timeout");
        }

        try
        {
            using var document = JsonDocument.Parse(request.ToArray());
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("protocolVersion", out var version) ||
                version.GetInt32() != 1)
            {
                return new WorkerResponse(1, "rejected", "unsupported_protocol");
            }

            if (resourceLimitsVerified is not null)
            {
                bool limitsVerified;
                try
                {
                    limitsVerified = resourceLimitsVerified();
                }
                catch
                {
                    limitsVerified = false;
                }

                if (!limitsVerified)
                {
                    return new WorkerResponse(1, "rejected", "worker_limits_unverified");
                }
            }

            // Parsing is intentionally not enabled until a supervisor and OS
            // resource ceilings are verified.
            return new WorkerResponse(1, "rejected", "worker_not_ready");
        }
        catch (JsonException)
        {
            return new WorkerResponse(1, "rejected", "invalid_request");
        }
        catch (Exception)
        {
            return new WorkerResponse(1, "rejected", "worker_protocol_error");
        }
    }

    public sealed record WorkerResponse(
        int ProtocolVersion,
        string Outcome,
        string ErrorCode,
        int PageCount = 0,
        string Text = "",
        bool RequiresOcr = false);
}

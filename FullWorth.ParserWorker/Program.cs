using System.Text.Json;

const int maxRequestBytes = 64 * 1024;
const int maxResponseBytes = 16 * 1024;

var response = await BuildResponseAsync(Console.OpenStandardInput(), maxRequestBytes);
var payload = JsonSerializer.SerializeToUtf8Bytes(response);
if (payload.Length > maxResponseBytes)
{
    payload = JsonSerializer.SerializeToUtf8Bytes(new WorkerResponse(1, "rejected", "worker_response_too_large"));
}

await Console.OpenStandardOutput().WriteAsync(payload);
await Console.Out.WriteLineAsync();

static async Task<WorkerResponse> BuildResponseAsync(Stream input, int maxBytes)
{
    var buffer = new byte[4096];
    using var request = new MemoryStream();

    while (true)
    {
        var read = await input.ReadAsync(buffer);
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

    try
    {
        using var document = JsonDocument.Parse(request.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("protocolVersion", out var version) ||
            version.GetInt32() != 1)
        {
            return new WorkerResponse(1, "rejected", "unsupported_protocol");
        }

        // Parsing is intentionally not enabled by this first boundary commit.
        // The API must continue using its existing in-process path until a
        // supervisor, bounded IPC contract, and OS resource ceilings are verified.
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

internal sealed record WorkerResponse(int ProtocolVersion, string Outcome, string ErrorCode);

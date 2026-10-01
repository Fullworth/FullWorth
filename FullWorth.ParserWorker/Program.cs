using System.Text.Json;

var response = await WorkerProtocol.BuildResponseAsync(
    Console.OpenStandardInput(),
    WorkerProtocol.MaxRequestBytes,
    TimeSpan.FromSeconds(5),
    WorkerResourceLimits.AreCurrentProcessLimitsEnforced);
var payload = JsonSerializer.SerializeToUtf8Bytes(response);
if (payload.Length > WorkerProtocol.MaxResponseBytes)
{
    payload = JsonSerializer.SerializeToUtf8Bytes(
        new WorkerResponse(1, "rejected", "worker_response_too_large"));
}
await Console.OpenStandardOutput().WriteAsync(payload);
await Console.Out.WriteLineAsync();

using System.Diagnostics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;

const string PdfContentType = "application/pdf";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

if (args.Length == 1 && args[0] == "--parse-pdf")
{
    var parsed = new PdfStatementTextParser().Extract(Console.OpenStandardInput());
    var output = JsonSerializer.SerializeToUtf8Bytes(parsed, jsonOptions);
    if (output.Length > WorkerProtocol.MaxResponseBytes)
    {
        output = JsonSerializer.SerializeToUtf8Bytes(
            new WorkerProtocol.WorkerResponse(1, "rejected", "response_too_large"),
            jsonOptions);
    }

    await Console.OpenStandardOutput().WriteAsync(output);
    return;
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = WorkerProtocol.MaxRequestBytes;
    options.Limits.MaxRequestLineSize = 8 * 1024;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;
});

var authentication =
    new ParserWorkerAuthentication(
        builder.Configuration["ParserWorker:AuthenticationToken"],
        builder.Environment.IsDevelopment());

var app = builder.Build();
using var requestGate = new SemaphoreSlim(1, 1);

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () =>
    WorkerResourceLimits.AreCurrentContainerLimitsEnforced()
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPost("/v1/pdf/extract", async (HttpContext context) =>
{
    if (!authentication.IsAuthorized(
            context.Request.Headers.Authorization,
            context.Request.Headers[ParserWorkerAuthentication.TimestampHeaderName],
            context.Request.Headers[ParserWorkerAuthentication.NonceHeaderName],
            context.Request.Headers[ParserWorkerAuthentication.SignatureHeaderName],
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(context.Request.ContentType, PdfContentType, StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
    }

    if (context.Request.ContentLength is > WorkerProtocol.MaxRequestBytes)
    {
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
    }

    if (!await requestGate.WaitAsync(0, context.RequestAborted))
    {
        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    }

    try
    {
        var input = await ReadBoundedAsync(context.Request.Body, context.RequestAborted);
        if (input is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var result = await RunParserProcessAsync(input, context.RequestAborted, jsonOptions);
        return Results.Bytes(result, "application/json");
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.Empty;
    }
    catch (TimeoutException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch
    {
        // Never return parser diagnostics or document content to the API caller.
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
    finally
    {
        requestGate.Release();
    }
});

app.Run();

static async Task<byte[]?> ReadBoundedAsync(Stream input, CancellationToken cancellationToken)
{
    using var output = new MemoryStream();
    var buffer = new byte[81920];
    while (true)
    {
        var read = await input.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            return output.ToArray();
        }

        if (read > WorkerProtocol.MaxRequestBytes - output.Length)
        {
            return null;
        }

        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
    }
}

static async Task<byte[]> RunParserProcessAsync(
    byte[] input,
    CancellationToken requestCancellation,
    JsonSerializerOptions jsonOptions)
{
    var dotnetPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(dotnetPath) || !Path.IsPathFullyQualified(dotnetPath))
    {
        throw new InvalidOperationException("The worker runtime path is unavailable.");
    }

    var startInfo = new ProcessStartInfo
    {
        FileName = dotnetPath,
        WorkingDirectory = AppContext.BaseDirectory,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };
    startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
    startInfo.ArgumentList.Add("--parse-pdf");

    // Do not pass the API/service environment to the parser child.
    startInfo.Environment.Clear();
    var dotnetRoot = Path.GetDirectoryName(dotnetPath);
    if (!string.IsNullOrWhiteSpace(dotnetRoot))
    {
        startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
    }
    startInfo.Environment["DOTNET_EnableDiagnostics"] = "0";

    using var process = new Process { StartInfo = startInfo };
    if (!process.Start())
    {
        throw new InvalidOperationException("The parser worker could not be started.");
    }

    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
    deadline.CancelAfter(TimeSpan.FromSeconds(20));
    var stdoutTask = ReadBoundedOutputAsync(process.StandardOutput.BaseStream, WorkerProtocol.MaxResponseBytes, deadline.Token);
    var stderrTask = DiscardedProcessOutput.DrainAsync(process.StandardError.BaseStream, deadline.Token);

    try
    {
        await process.StandardInput.BaseStream.WriteAsync(input, deadline.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(deadline.Token);

        var output = await stdoutTask;
        await stderrTask; // Drain with a fixed-size buffer; never retain parser diagnostics.
        if (process.ExitCode != 0 || output is null || output.Length == 0)
        {
            throw new InvalidOperationException("The parser worker failed.");
        }

        var parsed = JsonSerializer.Deserialize<WorkerProtocol.WorkerResponse>(output, jsonOptions);
        if (parsed is null ||
            parsed.ProtocolVersion != 1 ||
            parsed.PageCount is < 0 or > PdfStatementTextParser.MaxPages ||
            parsed.Text.Length > PdfStatementTextParser.MaxExtractedCharacters)
        {
            throw new InvalidOperationException("The parser worker returned an invalid response.");
        }

        return JsonSerializer.SerializeToUtf8Bytes(parsed, jsonOptions);
    }
    catch (OperationCanceledException) when (deadline.IsCancellationRequested)
    {
        TryKillWorkerTree(process);
        if (requestCancellation.IsCancellationRequested)
        {
            throw;
        }

        throw new TimeoutException();
    }
    finally
    {
        if (!process.HasExited)
        {
            TryKillWorkerTree(process);
        }
    }
}

static async Task<byte[]?> ReadBoundedOutputAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
{
    using var output = new MemoryStream();
    var buffer = new byte[8192];
    while (true)
    {
        var read = await stream.ReadAsync(buffer, cancellationToken);
        if (read == 0)
        {
            return output.ToArray();
        }

        if (read > maxBytes - output.Length)
        {
            return null;
        }

        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
    }
}

static void TryKillWorkerTree(Process process)
{
    try
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
    catch
    {
        // The worker failure is returned without exposing process diagnostics.
    }
}

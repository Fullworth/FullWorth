using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using FullWorth.ParserWorker.Ocr;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

const string PdfContentType = "application/pdf";
var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

if (args.Length == 1 && args[0] == "--containment-memory-child")
{
    /*
     * The parent starts this process before cgroup attachment, but does not
     * release this one-byte gate until the child is inside its document cgroup.
     */
    if (Console.OpenStandardInput().ReadByte() < 0)
    {
        Environment.ExitCode = 70;
        return;
    }

    var allocations = new List<nint>();
    var touchBuffer = new byte[1024 * 1024];
    Array.Fill(touchBuffer, (byte)0x5A);

    const int blockBytes = 16 * 1024 * 1024;
    const long maximumProbeBytes = 1024L * 1024 * 1024;
    long allocatedBytes = 0;

    while (allocatedBytes < maximumProbeBytes)
    {
        var block = Marshal.AllocHGlobal(blockBytes);
        allocations.Add(block);

        for (var offset = 0; offset < blockBytes; offset += touchBuffer.Length)
        {
            Marshal.Copy(
                touchBuffer,
                0,
                IntPtr.Add(block, offset),
                touchBuffer.Length);
        }

        allocatedBytes += blockBytes;
    }

    foreach (var block in allocations)
    {
        Marshal.FreeHGlobal(block);
    }

    Environment.ExitCode = 70;
    return;
}

if (args.Length == 1 && args[0] == "--containment-self-test")
{
    var evidence = await RunContainmentSelfTestAsync();
    var output = JsonSerializer.SerializeToUtf8Bytes(
        new
        {
            status = evidence.Success ? "ok" : "failed",
            code = evidence.Code,
            memoryMaxBytes = evidence.MemoryMaxBytes,
            memoryPeakBytes = evidence.MemoryPeakBytes,
            oomKillDelta = evidence.OomKillDelta
        },
        jsonOptions);

    await Console.OpenStandardOutput().WriteAsync(output);
    Environment.ExitCode = evidence.Success ? 0 : 70;
    return;
}

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

if (args.Length == 2 && args[0] == "--ocr-image")
{
    var mediaType = args[1];
    if (!string.Equals(
            mediaType,
            "image/png",
            StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(
            mediaType,
            "image/jpeg",
            StringComparison.OrdinalIgnoreCase))
    {
        Environment.ExitCode = 64;
        return;
    }

    /*
     * This process is started inside the document cgroup and receives no image
     * bytes until its parent has attached it to the one-shot image cgroup.
     * Native Tesseract initialization therefore cannot begin outside the image
     * CPU/memory ceiling.
     */
    var result = TesseractBillStatementOcrEngine.RunImageWorker(
        Console.OpenStandardInput(),
        mediaType,
        Path.Combine(
            AppContext.BaseDirectory,
            "tessdata"));

    var output = JsonSerializer.SerializeToUtf8Bytes(
        result,
        jsonOptions);

    if (output.Length > WorkerProtocol.MaxResponseBytes)
    {
        output = JsonSerializer.SerializeToUtf8Bytes(
            BillStatementOcrResult.Failure(1),
            jsonOptions);
    }

    await Console.OpenStandardOutput().WriteAsync(output);
    return;
}

if (args.Length == 3 && args[0] == "--ocr")
{
    var mediaType = args[1];
    var extension = args[2];
    if (!IsSupportedOcrDocument(mediaType, extension))
    {
        var rejected = JsonSerializer.SerializeToUtf8Bytes(
            BillStatementOcrResult.Failure(0),
            jsonOptions);
        await Console.OpenStandardOutput().WriteAsync(rejected);
        return;
    }

    /*
     * Read the bounded document before loading Tesseract.
     *
     * The supervisor does not write stdin until after this child has been
     * attached to its per-document cgroup. Blocking here closes the startup
     * race where native OCR could otherwise allocate memory before the hard
     * document ceiling became authoritative.
     */
    var input = await ReadBoundedAsync(
        Console.OpenStandardInput(),
        CancellationToken.None);
    if (input is null)
    {
        var rejected = JsonSerializer.SerializeToUtf8Bytes(
            BillStatementOcrResult.Failure(0),
            jsonOptions);
        await Console.OpenStandardOutput().WriteAsync(rejected);
        return;
    }

    using var engine = new TesseractBillStatementOcrEngine(
        Options.Create(new BillStatementOcrOptions
        {
            TessDataPath = Path.Combine(AppContext.BaseDirectory, "tessdata"),
            MaximumProcessingDuration = TimeSpan.FromSeconds(25)
        }),
        NullLogger<TesseractBillStatementOcrEngine>.Instance);
    using var inputStream = new MemoryStream(input, writable: false);
    var result = engine.TryExtract(
        inputStream,
        mediaType,
        extension);
    var output = JsonSerializer.SerializeToUtf8Bytes(result, jsonOptions);
    if (output.Length > WorkerProtocol.MaxResponseBytes)
    {
        output = JsonSerializer.SerializeToUtf8Bytes(
            BillStatementOcrResult.Failure(result.PageCount),
            jsonOptions);
    }

    await Console.OpenStandardOutput().WriteAsync(output);
    return;
}

var builder = WebApplication.CreateBuilder(args);
var tlsCertificate =
    builder.Environment.IsDevelopment()
        ? null
        : ParserWorkerTlsCertificate.CreateAndPublish(
            builder.Configuration[
                "ParserWorker:TlsCertificatePath"]);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = WorkerProtocol.MaxRequestBytes;
    options.Limits.MaxRequestLineSize = 8 * 1024;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024;

    if (tlsCertificate is not null)
    {
        options.ListenAnyIP(
            8081,
            listenOptions =>
                listenOptions.UseHttps(tlsCertificate));
    }
});

var authentication =
    new ParserWorkerAuthentication(
        builder.Configuration["ParserWorker:AuthenticationToken"],
        builder.Environment.IsDevelopment());

var app = builder.Build();
using var requestGate = new SemaphoreSlim(1, 1);

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () =>
    WorkerResourceLimits.AreCurrentContainerLimitsEnforced() &&
    DocumentProcessCgroup.IsDelegationReady()
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

app.MapPost("/v1/pdf/extract", async (HttpContext context) =>
{
    if (!authentication.IsAuthorized(
            context.Request.Headers.Authorization,
            context.Request.Headers[ParserWorkerAuthentication.TimestampHeaderName],
            context.Request.Headers[ParserWorkerAuthentication.NonceHeaderName],
            context.Request.Headers[ParserWorkerAuthentication.ContentSha256HeaderName],
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

        if (!authentication.HasExpectedContentHash(
                context.Request.Headers[
                    ParserWorkerAuthentication.ContentSha256HeaderName],
                input))
        {
            return Results.Unauthorized();
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

app.MapPost("/v1/ocr/extract", async (HttpContext context) =>
{
    var mediaType = context.Request.ContentType ?? string.Empty;
    var extension = context.Request.Headers["X-FullWorth-Ocr-Extension"].ToString();
    if (!authentication.IsAuthorized(
            context.Request.Headers.Authorization,
            context.Request.Headers[ParserWorkerAuthentication.TimestampHeaderName],
            context.Request.Headers[ParserWorkerAuthentication.NonceHeaderName],
            context.Request.Headers[ParserWorkerAuthentication.ContentSha256HeaderName],
            context.Request.Headers[ParserWorkerAuthentication.SignatureHeaderName],
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty))
    {
        return Results.Unauthorized();
    }

    if (!IsSupportedOcrDocument(mediaType, extension))
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

        if (!authentication.HasExpectedContentHash(
                context.Request.Headers[ParserWorkerAuthentication.ContentSha256HeaderName],
                input))
        {
            return Results.Unauthorized();
        }

        var result = await RunOcrProcessAsync(
            input,
            mediaType,
            extension,
            context.RequestAborted,
            jsonOptions);
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
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
    finally
    {
        requestGate.Release();
    }
});

app.Run();

static bool IsSupportedOcrDocument(string mediaType, string extension) =>
    string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(mediaType, "image/png", StringComparison.OrdinalIgnoreCase) &&
    string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
    string.Equals(mediaType, "image/jpeg", StringComparison.OrdinalIgnoreCase) &&
    (string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
     string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase));

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

    if (!DocumentProcessCgroup.TryAttachProcess(
            process.Id,
            "pdf",
            out var documentCgroup) ||
        documentCgroup is null)
    {
        TryKillWorkerTree(process);
        throw new InvalidOperationException("The parser worker could not establish document containment.");
    }

    using var containment = documentCgroup;
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

static async Task<byte[]> RunOcrProcessAsync(
    byte[] input,
    string mediaType,
    string extension,
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
    startInfo.ArgumentList.Add("--ocr");
    startInfo.ArgumentList.Add(mediaType);
    startInfo.ArgumentList.Add(extension);
    startInfo.Environment.Clear();
    OcrImageProcessCgroup.CopyConfiguredEnvironment(
        startInfo.Environment,
        Environment.GetEnvironmentVariable);
    var dotnetRoot = Path.GetDirectoryName(dotnetPath);
    if (!string.IsNullOrWhiteSpace(dotnetRoot))
    {
        startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
    }
    startInfo.Environment["DOTNET_EnableDiagnostics"] = "0";

    using var process = new Process { StartInfo = startInfo };
    if (!process.Start())
    {
        throw new InvalidOperationException("The OCR worker could not be started.");
    }

    if (!DocumentProcessCgroup.TryAttachProcess(
            process.Id,
            "ocr",
            out var documentCgroup) ||
        documentCgroup is null)
    {
        TryKillWorkerTree(process);
        throw new InvalidOperationException("The OCR worker could not establish document containment.");
    }

    using var containment = documentCgroup;

    try
    {
        // Prefer the disposable document child if the per-document cgroup
        // reaches its hard memory ceiling instead of sacrificing the supervisor.
        File.WriteAllText($"/proc/{process.Id}/oom_score_adj", "1000");
    }
    catch
    {
        // Container cgroup enforcement remains authoritative when proc tuning
        // is unavailable.
    }

    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation);
    deadline.CancelAfter(TimeSpan.FromSeconds(30));
    var stdoutTask = ReadBoundedOutputAsync(
        process.StandardOutput.BaseStream,
        WorkerProtocol.MaxResponseBytes,
        deadline.Token);
    var stderrTask = DiscardedProcessOutput.DrainAsync(
        process.StandardError.BaseStream,
        deadline.Token);

    try
    {
        await process.StandardInput.BaseStream.WriteAsync(input, deadline.Token);
        process.StandardInput.Close();
        await process.WaitForExitAsync(deadline.Token);
        var output = await stdoutTask;
        await stderrTask;
        if (process.ExitCode != 0 || output is null || output.Length == 0)
        {
            throw new InvalidOperationException("The OCR worker failed.");
        }

        var parsed = JsonSerializer.Deserialize<BillStatementOcrResult>(output, jsonOptions);
        if (parsed is null ||
            parsed.PageCount is < 0 or > 100 ||
            parsed.Text is null ||
            parsed.Text.Length > 250_000 ||
            parsed.MeanConfidence is < 0f or > 1f ||
            (parsed.IsUsable && string.IsNullOrWhiteSpace(parsed.Text)))
        {
            throw new InvalidOperationException("The OCR worker returned an invalid response.");
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

static async Task<(bool Success, string Code, long MemoryMaxBytes, long MemoryPeakBytes, long OomKillDelta)>
    RunContainmentSelfTestAsync()
{
    if (!OperatingSystem.IsLinux() ||
        !WorkerResourceLimits.AreCurrentContainerLimitsEnforced() ||
        !DocumentProcessCgroup.IsDelegationReady())
    {
        return (false, "containment_unavailable", 0, 0, 0);
    }

    var dotnetPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(dotnetPath) ||
        !Path.IsPathFullyQualified(dotnetPath))
    {
        return (false, "runtime_unavailable", 0, 0, 0);
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
    startInfo.ArgumentList.Add("--containment-memory-child");

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
        return (false, "probe_start_failed", 0, 0, 0);
    }

    if (!DocumentProcessCgroup.TryAttachProcess(
            process.Id,
            "ocr",
            out var containment) ||
        containment is null)
    {
        TryKillWorkerTree(process);
        return (false, "probe_attach_failed", 0, 0, 0);
    }

    var cgroupPath = containment.CgroupPath;
    var result =
        (Success: false,
         Code: "probe_failed",
         MemoryMaxBytes: 0L,
         MemoryPeakBytes: 0L,
         OomKillDelta: 0L);

    try
    {
        if (!containment.TryReadMemoryEvidence(
                out var memoryMaxBefore,
                out _,
                out var oomKillBefore))
        {
            result =
                (false, "probe_evidence_unavailable", 0, 0, 0);
        }
        else
        {
            var stdoutTask = DiscardedProcessOutput.DrainAsync(
                process.StandardOutput.BaseStream,
                CancellationToken.None);
            var stderrTask = DiscardedProcessOutput.DrainAsync(
                process.StandardError.BaseStream,
                CancellationToken.None);

            process.StandardInput.BaseStream.WriteByte(0x01);
            process.StandardInput.Close();

            using var deadline =
                new CancellationTokenSource(
                    TimeSpan.FromSeconds(20));

            var timedOut =
                false;

            try
            {
                await process.WaitForExitAsync(
                    deadline.Token);
            }
            catch (OperationCanceledException)
            {
                timedOut =
                    true;

                TryKillWorkerTree(
                    process);
            }

            await stdoutTask;
            await stderrTask;

            if (timedOut)
            {
                result =
                    (false, "probe_timeout", memoryMaxBefore, 0, 0);
            }
            else if (!containment.TryReadMemoryEvidence(
                         out var memoryMaxAfter,
                         out var memoryPeakAfter,
                         out var oomKillAfter))
            {
                result =
                    (false, "probe_evidence_unavailable", memoryMaxBefore, 0, 0);
            }
            else
            {
                var oomKillDelta =
                    Math.Max(
                        0,
                        oomKillAfter -
                        oomKillBefore);

                result =
                    memoryMaxAfter ==
                        memoryMaxBefore &&
                    process.ExitCode !=
                        0 &&
                    oomKillDelta >=
                        1
                        ? (true, "ok", memoryMaxAfter, memoryPeakAfter, oomKillDelta)
                        : (false, "probe_oom_not_observed", memoryMaxAfter, memoryPeakAfter, oomKillDelta);
            }
        }
    }
    catch
    {
        result =
            (false, "probe_failed", result.MemoryMaxBytes, result.MemoryPeakBytes, result.OomKillDelta);
    }
    finally
    {
        if (!process.HasExited)
        {
            TryKillWorkerTree(
                process);
        }

        containment.Dispose();
    }

    if (Directory.Exists(
            cgroupPath))
    {
        return (
            false,
            "probe_cleanup_failed",
            result.MemoryMaxBytes,
            result.MemoryPeakBytes,
            result.OomKillDelta);
    }

    return result;
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

        // A per-document cgroup cannot be removed while any killed descendant
        // is still draining out of it. Wait a short, bounded interval so the
        // containment lease can be deleted deterministically on failure paths.
        process.WaitForExit(milliseconds: 5_000);
    }
    catch
    {
        // The worker failure is returned without exposing process diagnostics.
        // Cgroup cleanup remains best effort if the runtime cannot confirm exit.
    }
}

using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TesseractOCR;
using TesseractOCR.Enums;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace FullWorth.ParserWorker.Ocr;

public interface IBillStatementOcrEngine
{
    BillStatementOcrResult TryExtract(
        Stream source,
        string mediaType,
        string fileExtension);
}

public sealed class TesseractBillStatementOcrEngine
    : IBillStatementOcrEngine,
      IDisposable
{
    private const int MaxPdfPages =
        100;

    private const int MaxImagesPerPdfPage =
        4;

    private const long MinimumPdfImagePixels =
        50_000;

    private const long MaximumPdfImagePixels =
        50_000_000;

    private const long MaximumTotalPdfImagePixels =
        500_000_000;

    private const int MaxImageBytes =
        20 * 1024 * 1024;

    private const int MaxExtractedCharacters =
        250_000;

    private const int MinimumUsefulCharacters =
        40;

    private const float MinimumPerImageConfidence =
        0.50f;

    private readonly object _engineLock =
        new();

    private readonly string _tessDataPath;

    private readonly float _minimumMeanConfidence;

    private readonly TimeSpan _maximumProcessingDuration;

    private readonly ILogger<TesseractBillStatementOcrEngine>
        _logger;

    private bool
        _disposed;

    public TesseractBillStatementOcrEngine(
        IOptions<BillStatementOcrOptions> options,
        ILogger<TesseractBillStatementOcrEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(
            options);

        _logger =
            logger;

        var configuredOptions =
            options.Value;

        _minimumMeanConfidence =
            configuredOptions.MinimumMeanConfidence;

        _maximumProcessingDuration =
            configuredOptions.MaximumProcessingDuration;

        if (_maximumProcessingDuration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "BillStatementOcr:MaximumProcessingDuration must be greater than zero.");
        }

        if (_minimumMeanConfidence is
            < 0f or > 1f)
        {
            throw new InvalidOperationException(
                "BillStatementOcr:MinimumMeanConfidence must be between 0 and 1.");
        }

        _tessDataPath =
            string.IsNullOrWhiteSpace(
                configuredOptions.TessDataPath)
                ? Path.Combine(
                    AppContext.BaseDirectory,
                    "tessdata")
                : Path.GetFullPath(
                    configuredOptions.TessDataPath);
    }

    public BillStatementOcrResult TryExtract(
        Stream source,
        string mediaType,
        string fileExtension)
    {
        ObjectDisposedException.ThrowIf(
            _disposed,
            this);

        ArgumentNullException.ThrowIfNull(
            source);

        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The OCR source stream is not readable.",
                nameof(source));
        }

        if (string.IsNullOrWhiteSpace(
                mediaType))
        {
            throw new ArgumentException(
                "Media type is required.",
                nameof(mediaType));
        }

        if (string.IsNullOrWhiteSpace(
                fileExtension))
        {
            throw new ArgumentException(
                "File extension is required.",
                nameof(fileExtension));
        }

        var deadline =
            new OcrProcessingDeadline(
                _maximumProcessingDuration);

        lock (_engineLock)
        {
            try
            {
                if (IsPdf(
                        mediaType,
                        fileExtension))
                {
                    return ExtractPdf(
                        source,
                        deadline);
                }

                if (IsImage(
                        mediaType,
                        fileExtension))
                {
                    return ExtractImage(
                        source,
                        mediaType,
                        deadline);
                }

                return BillStatementOcrResult.Failure(
                    pageCount:
                        0);
            }
            catch (Exception ex)
            {
                /*
                 * OCR remains a best-effort secondary extraction path.
                 *
                 * Never log document text, storage paths, OCR output,
                 * or native exception messages.
                 */
                _logger.LogWarning(
                    "Local statement OCR failed with {ExceptionType}.",
                    ex.GetType().Name);

                return BillStatementOcrResult.Failure(
                    pageCount:
                        0);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_engineLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed =
                true;
        }
    }

    internal static BillStatementOcrResult RunImageWorker(
        Stream source,
        string mediaType,
        string tessDataPath)
    {
        try
        {
            var bytes =
                ReadStreamWithLimit(
                    source,
                    MaxImageBytes);

            if (bytes.Length == 0 ||
                !EncodedOcrImageAdmission.TryAdmit(
                    bytes,
                    mediaType,
                    out _))
            {
                return BillStatementOcrResult.Failure(1);
            }

            var englishModelPath =
                Path.Combine(
                    tessDataPath,
                    "eng.traineddata");

            if (!File.Exists(englishModelPath))
            {
                return BillStatementOcrResult.Failure(1);
            }

            using var engine =
                new Engine(
                    tessDataPath,
                    Language.English,
                    EngineMode.Default);

            using var image =
                TesseractOCR.Pix.Image.LoadFromMemory(bytes);

            using var page =
                engine.Process(image);

            var text =
                NormalizeAndLimit(
                    page.Text ?? string.Empty);

            var confidence =
                Math.Clamp(
                    page.MeanConfidence,
                    0f,
                    1f);

            return new BillStatementOcrResult(
                text,
                1,
                confidence,
                text.Length >= MinimumUsefulCharacters &&
                confidence >= MinimumPerImageConfidence);
        }
        catch
        {
            // The document supervisor treats all native image-worker failures
            // identically and never receives native diagnostics or stack traces.
            return BillStatementOcrResult.Failure(1);
        }
    }

    private BillStatementOcrResult ExtractImage(
        Stream source,
        string mediaType,
        OcrProcessingDeadline deadline)
    {
        deadline.ThrowIfExpired();

        var bytes =
            ReadStreamWithLimit(
                source,
                MaxImageBytes);

        if (bytes.Length ==
                0 ||
            !EncodedOcrImageAdmission.TryAdmit(
                bytes,
                mediaType,
                out _))
        {
            return BillStatementOcrResult.Failure(
                pageCount:
                    1);
        }

        if (!TryRecognizeImage(
                bytes,
                mediaType,
                deadline,
                out var text,
                out var confidence))
        {
            return BillStatementOcrResult.Failure(
                pageCount:
                    1);
        }

        var normalizedText =
            NormalizeAndLimit(
                text);

        var isUsable =
            normalizedText.Length >=
                MinimumUsefulCharacters &&
            confidence >=
                _minimumMeanConfidence;

        return new BillStatementOcrResult(
            Text:
                normalizedText,

            PageCount:
                1,

            MeanConfidence:
                confidence,

            IsUsable:
                isUsable);
    }

    private BillStatementOcrResult ExtractPdf(
        Stream source,
        OcrProcessingDeadline deadline)
    {
        if (source.CanSeek)
        {
            source.Position =
                0;
        }

        using var document =
            PdfDocument.Open(
                source);

        var textBuilder =
            new StringBuilder();

        double weightedConfidence =
            0d;

        long confidenceWeight =
            0;

        var pageCount =
            0;

        var pixelBudget =
            new PdfOcrPixelBudget(
                MaximumTotalPdfImagePixels);

        foreach (var page in
                 document.GetPages())
        {
            deadline.ThrowIfExpired();
            pageCount++;

            if (pageCount >
                MaxPdfPages)
            {
                return BillStatementOcrResult.Failure(
                    pageCount:
                        pageCount);
            }

            var candidates =
                page.GetImages()
                    .Select(
                        image =>
                            new PdfImageCandidate(
                                image,
                                GetPixelCount(
                                    image)))
                    .Where(
                        candidate =>
                            candidate.PixelCount >=
                                MinimumPdfImagePixels &&
                            candidate.PixelCount <=
                                MaximumPdfImagePixels)
                    .OrderByDescending(
                        candidate =>
                            candidate.PixelCount)
                    .Take(
                        MaxImagesPerPdfPage)
                    .ToList();

            var pixelBudgetExceeded =
                false;

            foreach (var candidate in
                     candidates)
            {
                /*
                 * Count declared pixels before image decoding or native OCR.
                 * This caps cumulative OCR work even when images yield no text.
                 */
                if (!pixelBudget.TryConsume(
                        candidate.PixelCount))
                {
                    pixelBudgetExceeded =
                        true;

                    break;
                }

                var imageBytes =
                    GetPdfImageBytes(
                        candidate.Image);

                if (imageBytes is null ||
                    imageBytes.Length ==
                        0 ||
                    imageBytes.Length >
                        MaxImageBytes)
                {
                    continue;
                }

                if (!PdfImageMemoryAdmission.TryEstimate(
                        candidate.PixelCount,
                        imageBytes.Length,
                        out _))
                {
                    continue;
                }

                var imageMediaType =
                    DetectEncodedImageMediaType(
                        imageBytes);

                if (imageMediaType is null ||
                    !TryRecognizeImage(
                        imageBytes,
                        imageMediaType,
                        deadline,
                        out var imageText,
                        out var imageConfidence))
                {
                    continue;
                }

                var normalizedImageText =
                    NormalizeAndLimit(
                        imageText);

                if (normalizedImageText.Length <
                        MinimumUsefulCharacters ||
                    imageConfidence <
                        MinimumPerImageConfidence)
                {
                    continue;
                }

                AppendTextWithLimit(
                    textBuilder,
                    normalizedImageText);

                weightedConfidence +=
                    imageConfidence *
                    normalizedImageText.Length;

                confidenceWeight +=
                    normalizedImageText.Length;

                if (textBuilder.Length >=
                    MaxExtractedCharacters)
                {
                    break;
                }
            }

            if (pixelBudgetExceeded ||
                textBuilder.Length >=
                    MaxExtractedCharacters)
            {
                break;
            }
        }

        if (textBuilder.Length ==
                0 ||
            confidenceWeight ==
                0)
        {
            return BillStatementOcrResult.Failure(
                pageCount:
                    pageCount);
        }

        var meanConfidence =
            (float)
            (weightedConfidence /
             confidenceWeight);

        meanConfidence =
            Math.Clamp(
                meanConfidence,
                0f,
                1f);

        var finalText =
            textBuilder
                .ToString()
                .Trim();

        var isUsable =
            finalText.Length >=
                MinimumUsefulCharacters &&
            meanConfidence >=
                _minimumMeanConfidence;

        return new BillStatementOcrResult(
            Text:
                finalText,

            PageCount:
                pageCount,

            MeanConfidence:
                meanConfidence,

            IsUsable:
                isUsable);
    }

    private bool TryRecognizeImage(
        byte[] imageBytes,
        string mediaType,
        OcrProcessingDeadline deadline,
        out string text,
        out float confidence)
    {
        text = string.Empty;
        confidence = 0f;

        Process? process = null;
        OcrImageProcessCgroup? imageScope = null;

        try
        {
            deadline.ThrowIfExpired();

            var dotnetPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(dotnetPath) ||
                !Path.IsPathFullyQualified(dotnetPath))
            {
                return false;
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
            startInfo.ArgumentList.Add(
                Assembly.GetExecutingAssembly().Location);
            startInfo.ArgumentList.Add("--ocr-image");
            startInfo.ArgumentList.Add(mediaType);
            startInfo.Environment.Clear();

            var dotnetRoot = Path.GetDirectoryName(dotnetPath);
            if (!string.IsNullOrWhiteSpace(dotnetRoot))
            {
                startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            }

            startInfo.Environment["DOTNET_EnableDiagnostics"] = "0";

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return false;
            }

            /*
             * The image worker inherits the document cgroup but blocks on
             * stdin. Attach it to its one-shot image cgroup before releasing
             * any encoded image bytes or initializing native Tesseract state.
             */
            if (OperatingSystem.IsLinux() &&
                (!OcrImageProcessCgroup.TryAttachProcess(
                    process.Id,
                    out imageScope,
                    out var failureCode) ||
                 imageScope is null))
            {
                TryKillImageWorker(process);
                throw new OcrImageContainmentException(failureCode);
            }

            var observationDelay =
                OcrImageProcessCgroup.GetConfiguredObservationDelay();
            if (observationDelay > TimeSpan.Zero)
            {
                /*
                 * CI may hold the already-contained, stdin-blocked image worker
                 * briefly so the host can inspect the live kernel scope.
                 * Production defaults to zero and no image bytes are released
                 * until after this bounded observation window.
                 */
                Thread.Sleep(observationDelay);
            }

            try
            {
                File.WriteAllText(
                    $"/proc/{process.Id}/oom_score_adj",
                    "1000");
            }
            catch
            {
                // The image cgroup remains authoritative when proc tuning is unavailable.
            }

            var stdoutTask =
                ReadBoundedImageWorkerOutputAsync(
                    process.StandardOutput.BaseStream);
            var stderrTask =
                DiscardedProcessOutput.DrainAsync(
                    process.StandardError.BaseStream);

            process.StandardInput.BaseStream.Write(
                imageBytes,
                0,
                imageBytes.Length);
            process.StandardInput.Close();

            while (!process.WaitForExit(25))
            {
                deadline.ThrowIfExpired();
            }

            var output =
                stdoutTask.GetAwaiter().GetResult();

            stderrTask.GetAwaiter().GetResult();

            if (process.ExitCode != 0 ||
                output is null ||
                output.Length == 0)
            {
                return false;
            }

            var parsed =
                JsonSerializer.Deserialize<BillStatementOcrResult>(
                    output,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

            if (parsed is null ||
                parsed.PageCount != 1 ||
                parsed.Text is null ||
                parsed.Text.Length > MaxExtractedCharacters ||
                parsed.MeanConfidence is < 0f or > 1f)
            {
                return false;
            }

            deadline.ThrowIfExpired();

            text = parsed.Text;
            confidence = parsed.MeanConfidence;

            return text.Length > 0;
        }
        catch (BillStatementOcrTimeoutException)
        {
            if (process is not null)
            {
                TryKillImageWorker(process);
            }

            throw;
        }
        catch (OcrImageContainmentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                "A statement image worker failed with {ExceptionType}.",
                ex.GetType().Name);

            return false;
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited)
                {
                    TryKillImageWorker(process);
                }

                process.Dispose();
            }

            imageScope?.Dispose();
        }
    }

    private static async Task<byte[]?> ReadBoundedImageWorkerOutputAsync(
        Stream source)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];

        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return output.ToArray();
            }

            if (read > WorkerProtocol.MaxResponseBytes - output.Length)
            {
                return null;
            }

            await output.WriteAsync(buffer.AsMemory(0, read));
        }
    }

    private static void TryKillImageWorker(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(5_000);
        }
        catch
        {
            // The outer document supervisor still owns the document deadline.
        }
    }

    private static string? DetectEncodedImageMediaType(byte[] bytes)
    {
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 &&
            bytes[1] == 0x50 &&
            bytes[2] == 0x4E &&
            bytes[3] == 0x47 &&
            bytes[4] == 0x0D &&
            bytes[5] == 0x0A &&
            bytes[6] == 0x1A &&
            bytes[7] == 0x0A)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 &&
            bytes[0] == 0xFF &&
            bytes[1] == 0xD8 &&
            bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        return null;
    }

    private static byte[]? GetPdfImageBytes(
        IPdfImage image)
    {
        if (image.TryGetPng(
                out var pngBytes) &&
            pngBytes is
            {
                Length: > 0
            })
        {
            return pngBytes;
        }

        /*
         * PdfPig exposes the original JPEG file in RawMemory for
         * JPEG-backed PDF images.
         *
         * Do not feed arbitrary decoded PDF bitmap bytes into
         * Tesseract as though they were standalone image files.
         */
        var rawMemory =
            image.RawMemory;

        if (rawMemory.Length <
            3)
        {
            return null;
        }

        var rawSpan =
            rawMemory.Span;

        var isJpeg =
            rawSpan[0] ==
                0xFF &&
            rawSpan[1] ==
                0xD8 &&
            rawSpan[2] ==
                0xFF;

        if (!isJpeg)
        {
            return null;
        }

        return rawMemory.ToArray();
    }

    private static long GetPixelCount(
        IPdfImage image)
    {
        var width =
            Math.Max(
                0,
                image.WidthInSamples);

        var height =
            Math.Max(
                0,
                image.HeightInSamples);

        return
            (long)width *
            height;
    }

    private static void AppendTextWithLimit(
        StringBuilder destination,
        string text)
    {
        if (destination.Length >=
            MaxExtractedCharacters)
        {
            return;
        }

        if (destination.Length >
            0)
        {
            destination.AppendLine();
            destination.AppendLine();
        }

        var remainingCharacters =
            MaxExtractedCharacters -
            destination.Length;

        if (remainingCharacters <=
            0)
        {
            return;
        }

        if (text.Length <=
            remainingCharacters)
        {
            destination.Append(
                text);

            return;
        }

        destination.Append(
            text.AsSpan(
                0,
                remainingCharacters));
    }

    private static byte[] ReadStreamWithLimit(
        Stream source,
        int maximumBytes)
    {
        if (source.CanSeek)
        {
            if (source.Length >
                maximumBytes)
            {
                return [];
            }

            source.Position =
                0;
        }

        using var destination =
            new MemoryStream();

        var buffer =
            new byte[81920];

        var totalBytes =
            0;

        while (true)
        {
            var bytesRead =
                source.Read(
                    buffer,
                    0,
                    buffer.Length);

            if (bytesRead ==
                0)
            {
                break;
            }

            totalBytes +=
                bytesRead;

            if (totalBytes >
                maximumBytes)
            {
                return [];
            }

            destination.Write(
                buffer,
                0,
                bytesRead);
        }

        return destination.ToArray();
    }

    private static string NormalizeAndLimit(
        string value)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var normalized =
            value
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n')
                .Trim();

        if (normalized.Length <=
            MaxExtractedCharacters)
        {
            return normalized;
        }

        return normalized[
            ..MaxExtractedCharacters];
    }

    private static bool IsPdf(
        string mediaType,
        string fileExtension)
    {
        return
            string.Equals(
                mediaType,
                "application/pdf",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                fileExtension,
                ".pdf",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(
        string mediaType,
        string fileExtension)
    {
        var isPng =
            string.Equals(
                mediaType,
                "image/png",
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                fileExtension,
                ".png",
                StringComparison.OrdinalIgnoreCase);

        var isJpeg =
            string.Equals(
                mediaType,
                "image/jpeg",
                StringComparison.OrdinalIgnoreCase) &&
            (
                string.Equals(
                    fileExtension,
                    ".jpg",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    fileExtension,
                    ".jpeg",
                    StringComparison.OrdinalIgnoreCase)
            );

        return
            isPng ||
            isJpeg;
    }

    private sealed record PdfImageCandidate(
        IPdfImage Image,
        long PixelCount);
}

public sealed record BillStatementOcrResult(
    string Text,
    int PageCount,
    float MeanConfidence,
    bool IsUsable,
    string? FailureCode = null)
{
    public static BillStatementOcrResult Failure(
        int pageCount,
        string? failureCode = null)
    {
        return new BillStatementOcrResult(
            Text:
                string.Empty,

            PageCount:
                pageCount,

            MeanConfidence:
                0f,

            IsUsable:
                false,

            FailureCode:
                failureCode);
    }
}

public sealed class BillStatementOcrOptions
{
    public const string SectionName =
        "BillStatementOcr";

    public string? TessDataPath { get; set; }

    public float MinimumMeanConfidence { get; set; } =
        0.80f;

    public TimeSpan MaximumProcessingDuration { get; set; } =
        TimeSpan.FromSeconds(30);
}

internal sealed class OcrProcessingDeadline
{
    private readonly Stopwatch _stopwatch =
        Stopwatch.StartNew();

    private readonly TimeSpan _maximumDuration;

    public OcrProcessingDeadline(TimeSpan maximumDuration)
    {
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration),
                "The OCR processing duration must be greater than zero.");
        }

        _maximumDuration = maximumDuration;
    }

    public void ThrowIfExpired()
    {
        if (_stopwatch.Elapsed >= _maximumDuration)
        {
            throw new BillStatementOcrTimeoutException();
        }
    }
}

internal sealed class BillStatementOcrTimeoutException : Exception
{
}

internal sealed class OcrImageContainmentException : Exception
{
    public OcrImageContainmentException(string code)
        : base(code)
    {
        Code = string.IsNullOrWhiteSpace(code)
            ? "image_containment_unavailable"
            : code;
    }

    public string Code { get; }
}

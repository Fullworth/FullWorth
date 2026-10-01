namespace FullWorth.API.Services.Statements;

internal sealed class PdfOcrPixelBudget
{
    private readonly long _maximumPixels;

    private long _consumedPixels;

    internal PdfOcrPixelBudget(
        long maximumPixels)
    {
        if (maximumPixels <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPixels));
        }

        _maximumPixels =
            maximumPixels;
    }

    internal long ConsumedPixels =>
        _consumedPixels;

    internal bool TryConsume(
        long pixels)
    {
        if (pixels <= 0 ||
            pixels > _maximumPixels - _consumedPixels)
        {
            return false;
        }

        _consumedPixels +=
            pixels;

        return true;
    }
}

/// <summary>
/// Performs a conservative pre-decode admission check for PDF OCR images.
/// This estimate is not a hard peak-memory bound: native PDF/image decoders and
/// Tesseract still run in-process and may allocate outside this calculation.
/// </summary>
internal static class PdfImageMemoryAdmission
{
    internal const long EstimatedBytesPerPixel = 8;

    internal const long MaximumEstimatedWorkingSetBytes =
        256L * 1024 * 1024;

    internal static bool TryEstimate(
        long pixelCount,
        long encodedBytes,
        out long estimatedBytes)
    {
        estimatedBytes =
            0;

        if (pixelCount <= 0 ||
            encodedBytes < 0)
        {
            return false;
        }

        var remainingBytes =
            MaximumEstimatedWorkingSetBytes - encodedBytes;

        if (remainingBytes < 0 ||
            pixelCount > remainingBytes / EstimatedBytesPerPixel)
        {
            return false;
        }

        estimatedBytes =
            (pixelCount * EstimatedBytesPerPixel) + encodedBytes;

        return true;
    }
}

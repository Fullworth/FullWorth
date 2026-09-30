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

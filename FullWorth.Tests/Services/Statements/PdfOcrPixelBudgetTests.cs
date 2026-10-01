using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services.Statements;

public sealed class PdfOcrPixelBudgetTests
{
    [Fact]
    public void TryConsume_AllowsExactLimitAndRejectsOverflow()
    {
        var budget =
            new PdfOcrPixelBudget(
                maximumPixels: 100);

        Assert.True(
            budget.TryConsume(
                60));

        Assert.True(
            budget.TryConsume(
                40));

        Assert.False(
            budget.TryConsume(
                1));

        Assert.Equal(
            100,
            budget.ConsumedPixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TryConsume_RejectsNonPositiveImagePixels(
        long pixels)
    {
        var budget =
            new PdfOcrPixelBudget(
                maximumPixels: 100);

        Assert.False(
            budget.TryConsume(
                pixels));

        Assert.Equal(
            0,
            budget.ConsumedPixels);
    }

    [Fact]
    public void TryConsume_RejectsImageLargerThanRemainingBudgetWithoutOverflow()
    {
        var budget =
            new PdfOcrPixelBudget(
                maximumPixels: long.MaxValue);

        Assert.True(
            budget.TryConsume(
                long.MaxValue - 1));

        Assert.False(
            budget.TryConsume(
                2));

        Assert.Equal(
            long.MaxValue - 1,
            budget.ConsumedPixels);
    }

    [Fact]
    public void TryEstimate_AllowsEstimateAtTheAdmissionLimit()
    {
        var encodedBytes =
            1_024;

        var pixelCount =
            (PdfImageMemoryAdmission.MaximumEstimatedWorkingSetBytes -
             encodedBytes) /
            PdfImageMemoryAdmission.EstimatedBytesPerPixel;

        Assert.True(
            PdfImageMemoryAdmission.TryEstimate(
                pixelCount,
                encodedBytes,
                out var estimatedBytes));

        Assert.Equal(
            PdfImageMemoryAdmission.MaximumEstimatedWorkingSetBytes,
            estimatedBytes);
    }

    [Fact]
    public void TryEstimate_RejectsEstimateAboveAdmissionLimit()
    {
        var encodedBytes =
            1_024;

        var pixelCount =
            ((PdfImageMemoryAdmission.MaximumEstimatedWorkingSetBytes -
              encodedBytes) /
             PdfImageMemoryAdmission.EstimatedBytesPerPixel) +
            1;

        Assert.False(
            PdfImageMemoryAdmission.TryEstimate(
                pixelCount,
                encodedBytes,
                out var estimatedBytes));

        Assert.Equal(
            0,
            estimatedBytes);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void TryEstimate_RejectsInvalidInputs(
        long pixelCount,
        long encodedBytes)
    {
        Assert.False(
            PdfImageMemoryAdmission.TryEstimate(
                pixelCount,
                encodedBytes,
                out var estimatedBytes));

        Assert.Equal(
            0,
            estimatedBytes);
    }

    [Fact]
    public void TryEstimate_AllowsAConservativeSubLimitImage()
    {
        Assert.True(
            PdfImageMemoryAdmission.TryEstimate(
                pixelCount:
                    20_000_000,
                encodedBytes:
                    1_024,
                out var estimatedBytes));

        Assert.Equal(
            (20_000_000 * PdfImageMemoryAdmission.EstimatedBytesPerPixel) +
            1_024,
            estimatedBytes);
    }

}

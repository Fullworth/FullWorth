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
}

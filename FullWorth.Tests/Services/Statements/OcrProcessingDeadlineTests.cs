using FullWorth.API.Services.Statements;


namespace FullWorth.Tests.Services.Statements;


public sealed class OcrProcessingDeadlineTests
{
    [Fact]
    public void ThrowIfExpired_ThrowsAfterBudgetIsConsumed()
    {
        var deadline =
            new OcrProcessingDeadline(
                TimeSpan.FromTicks(1));

        Thread.SpinWait(10_000);

        Assert.Throws<BillStatementOcrTimeoutException>(
            deadline.ThrowIfExpired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveDurations(
        int milliseconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OcrProcessingDeadline(
                TimeSpan.FromMilliseconds(milliseconds)));
    }
}

using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class StatementProcessingFailureBackoffTests
{
    [Fact]
    public void PoisonItemCanBeSkippedWhileHealthyUploadsRemainEligible()
    {
        var tracker = new StatementProcessingFailureBackoff();
        var now = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        var stuck = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        tracker.Defer(stuck, now);

        var blocked = tracker.DeferredIds(now.AddMinutes(10));
        Assert.Contains(stuck, blocked);
        Assert.DoesNotContain(healthy, blocked);
        Assert.Equal(new[] { healthy },
            new[] { stuck, healthy }.Where(id => !blocked.Contains(id)).ToArray());
    }

    [Fact]
    public void TransientFailureIsEligibleAgainAfterBackoff()
    {
        var tracker = new StatementProcessingFailureBackoff();
        var now = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        tracker.Defer(id, now);
        Assert.Contains(id, tracker.DeferredIds(now.AddMinutes(59)));
        Assert.Empty(tracker.DeferredIds(now.AddHours(1)));
    }

    [Fact]
    public void InvalidUploadIdCannotBeDeferred()
    {
        var tracker = new StatementProcessingFailureBackoff();
        Assert.Throws<ArgumentException>(() => tracker.Defer(Guid.Empty, DateTimeOffset.UtcNow));
    }
}

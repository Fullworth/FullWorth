using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlanningPaydayAlertGatewayTests
{
    [Fact]
    public async Task EnsureAsync_CreatesOwnerScopedUserLevelPaydayAlert()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlanningPaydayAlertGateway(
                dbContext);

        var userId =
            Guid.NewGuid();

        var sourceEventId =
            Guid.NewGuid();

        var now =
            new DateTimeOffset(
                2026,
                9,
                27,
                21,
                0,
                0,
                TimeSpan.FromHours(
                    -6));

        await gateway.EnsureAsync(
            userId,
            Request(
                sourceEventId),
            now);

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());

        Assert.Equal(
            userId,
            alert.UserId);

        Assert.Null(
            alert.BillStreamId);

        Assert.Null(
            alert.BillChangeId);

        Assert.Equal(
            sourceEventId,
            alert.SourceEventId);

        Assert.Equal(
            BillAlertType.PaydayPlan,
            alert.AlertType);

        Assert.Equal(
            BillAlertSeverity.Info,
            alert.Severity);

        Assert.Equal(
            "Payday plan ready",
            alert.Title);

        Assert.Equal(
            "$250 of this paycheck is planned for upcoming bills.",
            alert.Message);

        Assert.False(
            alert.IsRead);

        Assert.False(
            alert.IsDismissed);

        Assert.Equal(
            now,
            alert.CreatedAtUtc);

        Assert.Equal(
            now,
            alert.UpdatedAtUtc);
    }

    [Fact]
    public async Task EnsureAsync_IdenticalReplayDoesNotDuplicateOrResurfaceAlert()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlanningPaydayAlertGateway(
                dbContext);

        var userId =
            Guid.NewGuid();

        var sourceEventId =
            Guid.NewGuid();

        var firstNow =
            DateTimeOffset.UtcNow;

        await gateway.EnsureAsync(
            userId,
            Request(
                sourceEventId),
            firstNow);

        var alert =
            Assert.Single(
                dbContext.BillAlerts);

        alert.IsRead =
            true;

        alert.IsDismissed =
            true;

        await dbContext.SaveChangesAsync();

        await gateway.EnsureAsync(
            userId,
            Request(
                sourceEventId),
            firstNow.AddHours(
                1));

        var persisted =
            Assert.Single(
                await dbContext.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());

        Assert.True(
            persisted.IsRead);

        Assert.True(
            persisted.IsDismissed);

        Assert.Equal(
            firstNow,
            persisted.CreatedAtUtc);

        Assert.Equal(
            firstNow,
            persisted.UpdatedAtUtc);
    }

    [Fact]
    public async Task EnsureAsync_SameOpaqueEventForDifferentUsers_RemainsIsolated()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlanningPaydayAlertGateway(
                dbContext);

        var sourceEventId =
            Guid.NewGuid();

        await gateway.EnsureAsync(
            Guid.NewGuid(),
            Request(
                sourceEventId),
            DateTimeOffset.UtcNow);

        await gateway.EnsureAsync(
            Guid.NewGuid(),
            Request(
                sourceEventId),
            DateTimeOffset.UtcNow);

        Assert.Equal(
            2,
            await dbContext.BillAlerts
                .CountAsync());
    }

    [Fact]
    public async Task EnsureAsync_ConflictingReplayFailsClosed()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlanningPaydayAlertGateway(
                dbContext);

        var userId =
            Guid.NewGuid();

        var sourceEventId =
            Guid.NewGuid();

        await gateway.EnsureAsync(
            userId,
            Request(
                sourceEventId),
            DateTimeOffset.UtcNow);

        var conflicting =
            Request(
                sourceEventId) with
            {
                Message =
                    "Different planning guidance."
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                gateway.EnsureAsync(
                    userId,
                    conflicting,
                    DateTimeOffset.UtcNow));

        var persisted =
            Assert.Single(
                await dbContext.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());

        Assert.Equal(
            "$250 of this paycheck is planned for upcoming bills.",
            persisted.Message);
    }

    [Fact]
    public async Task EnsureAsync_RejectsMissingIdentityAndOversizedContent()
    {
        var options =
            Options();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlanningPaydayAlertGateway(
                dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.EnsureAsync(
                    Guid.Empty,
                    Request(
                        Guid.NewGuid()),
                    DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.EnsureAsync(
                    Guid.NewGuid(),
                    Request(
                        Guid.Empty),
                    DateTimeOffset.UtcNow));

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.EnsureAsync(
                    Guid.NewGuid(),
                    Request(
                        Guid.NewGuid()) with
                    {
                        Title =
                            new string(
                                'x',
                                301)
                    },
                    DateTimeOffset.UtcNow));
    }

    private static DbContextOptions<FullWorthDbContext> Options() =>
        new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(
                $"payday-alert-{Guid.NewGuid():N}")
            .Options;

    private static PlanningPaydayAlertRequest Request(
        Guid sourceEventId) =>
        new(
            sourceEventId,
            PlanningPaydayAlertSeverity.Info,
            "Payday plan ready",
            "$250 of this paycheck is planned for upcoming bills.");
}

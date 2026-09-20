using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillStatementEvidenceAlertServiceTests
{
    [Fact]
    public async Task PreloadedAlerts_MustBelongToRequestedChange()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var currentStatementId =
            Guid.NewGuid();

        var change =
            new BillChangeEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                CurrentStatementId =
                    currentStatementId,

                ChangeType =
                    BillChangeType.TotalIncrease
            };

        var unrelatedAlert =
            new BillAlertEntity
            {
                UserId =
                    Guid.NewGuid(),

                BillStreamId =
                    billStreamId,

                BillChangeId =
                    change.Id,

                AlertType =
                    BillAlertType.NewFee,

                Severity =
                    BillAlertSeverity.Warning,

                Title =
                    "Unrelated alert",

                Message =
                    "Unrelated"
            };

        var service =
            new BillStatementEvidenceAlertService(
                dbContext);

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () =>
                service.ReconcileAsync(
                    userId,
                    billStreamId,
                    "Example Provider",
                    change,
                    [],
                    [],
                    DateTimeOffset.UtcNow,
                    preloadedAlerts:
                        [unrelatedAlert]));
    }

    [Fact]
    public async Task EmptyPreloadedAlerts_AreAcceptedForOwnedChange()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var change =
            new BillChangeEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                CurrentStatementId =
                    Guid.NewGuid(),

                ChangeType =
                    BillChangeType.TotalIncrease
            };

        var service =
            new BillStatementEvidenceAlertService(
                dbContext);

        await service.ReconcileAsync(
            userId,
            billStreamId,
            "Example Provider",
            change,
            [],
            [],
            DateTimeOffset.UtcNow,
            preloadedAlerts:
                []);

        Assert.Empty(
            dbContext.BillAlerts.Local);
    }

    private static FullWorthDbContext
        CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<
                    FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"statement-evidence-{Guid.NewGuid():N}")
                .Options);
    }
}

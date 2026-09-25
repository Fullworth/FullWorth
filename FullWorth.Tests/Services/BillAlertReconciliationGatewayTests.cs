using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillAlertReconciliationGatewayTests
{
    [Fact]
    public async Task StageReconciliation_DoesNotCommitBeforeOwningUnitOfWorkSaves()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BillStreams.Add(
                stream);

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new BillAlertReconciliationGateway(
                dbContext);

        await gateway.StageReconciliationAsync(
            userId,
            stream.Id,
            [
                PaymentScope(
                    "Midco payment due Sep 30, 2026",
                    "$99.00 is due on Sep 30, 2026.")
            ],
            [],
            DateTimeOffset.UtcNow);

        await using (var beforeCommit =
                     new FullWorthDbContext(
                         options))
        {
            Assert.Empty(
                await beforeCommit.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());
        }

        await dbContext.SaveChangesAsync();

        await using var afterCommit =
            new FullWorthDbContext(
                options);

        Assert.Single(
            await afterCommit.BillAlerts
                .AsNoTracking()
                .ToListAsync());
    }

    [Fact]
    public async Task OtherUsersBillStream_IsRejected()
    {
        var options =
            CreateOptions();

        var ownerUserId =
            Guid.NewGuid();

        var callerUserId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                ownerUserId);

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BillStreams.Add(
                stream);

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new BillAlertReconciliationGateway(
                dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                gateway.StageReconciliationAsync(
                    callerUserId,
                    stream.Id,
                    [
                        PaymentScope(
                            "Private payment due",
                            "Private")
                    ],
                    [],
                    DateTimeOffset.UtcNow));

        Assert.Empty(
            dbContext.BillAlerts.Local);
    }

    [Fact]
    public async Task PaymentDueUpsert_RetainsOlderDueEvent()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BillStreams.Add(
                stream);

            seed.BillAlerts.Add(
                CreateAlert(
                    userId,
                    stream.Id,
                    billChangeId:
                        null,
                    BillAlertType.PaymentDue,
                    "Midco payment due Sep 1, 2026",
                    "Old due event",
                    DateTimeOffset.UtcNow.AddDays(
                        -30)));

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new BillAlertReconciliationGateway(
                dbContext);

        await gateway.StageReconciliationAsync(
            userId,
            stream.Id,
            [
                PaymentScope(
                    "Midco payment due Oct 1, 2026",
                    "New due event")
            ],
            [],
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        var alerts =
            await dbContext.BillAlerts
                .AsNoTracking()
                .OrderBy(
                    alert =>
                        alert.CreatedAtUtc)
                .ToListAsync();

        Assert.Equal(
            2,
            alerts.Count);

        Assert.Contains(
            alerts,
            alert =>
                alert.Title ==
                    "Midco payment due Sep 1, 2026");

        Assert.Contains(
            alerts,
            alert =>
                alert.Title ==
                    "Midco payment due Oct 1, 2026");
    }

    [Fact]
    public async Task SingleSlot_UpdatesPrimaryAndRemovesDuplicates()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        var stream =
            CreateBillStream(
                userId);

        var billChangeId =
            Guid.NewGuid();

        var primary =
            CreateAlert(
                userId,
                stream.Id,
                billChangeId,
                BillAlertType.BillIncrease,
                "Old increase",
                "Old message",
                DateTimeOffset.UtcNow.AddMinutes(
                    -10));

        primary.IsRead =
            true;

        primary.IsDismissed =
            true;

        var duplicate =
            CreateAlert(
                userId,
                stream.Id,
                billChangeId,
                BillAlertType.BillIncrease,
                "Duplicate",
                "Duplicate message",
                DateTimeOffset.UtcNow.AddMinutes(
                    -5));

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BillStreams.Add(
                stream);

            seed.BillAlerts.AddRange(
                primary,
                duplicate);

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new BillAlertReconciliationGateway(
                dbContext);

        await gateway.StageReconciliationAsync(
            userId,
            stream.Id,
            [
                new BillAlertReconciliationScope(
                    BillChangeId:
                        billChangeId,

                    ManagedAlertTypes:
                        [
                            BillAlertContractType.BillIncrease,
                            BillAlertContractType.BillDecrease
                        ],

                    DesiredAlerts:
                        [
                            new BillAlertDesiredState(
                                BillAlertContractType.BillDecrease,
                                BillAlertContractSeverity.Info,
                                "New decrease",
                                "New message")
                        ],

                    Mode:
                        BillAlertReconciliationMode.SingleManagedSlot)
            ],
            [],
            DateTimeOffset.UtcNow);

        await dbContext.SaveChangesAsync();

        var alert =
            Assert.Single(
                await dbContext.BillAlerts
                    .AsNoTracking()
                    .ToListAsync());

        Assert.Equal(
            primary.Id,
            alert.Id);

        Assert.Equal(
            BillAlertType.BillDecrease,
            alert.AlertType);

        Assert.Equal(
            "New decrease",
            alert.Title);

        Assert.Equal(
            "New message",
            alert.Message);

        Assert.False(
            alert.IsRead);

        Assert.False(
            alert.IsDismissed);
    }

    private static BillAlertReconciliationScope
        PaymentScope(
            string title,
            string message)
    {
        return new BillAlertReconciliationScope(
            BillChangeId:
                null,

            ManagedAlertTypes:
                [
                    BillAlertContractType.PaymentDue
                ],

            DesiredAlerts:
                [
                    new BillAlertDesiredState(
                        BillAlertContractType.PaymentDue,
                        BillAlertContractSeverity.Info,
                        title,
                        message)
                ],

            Mode:
                BillAlertReconciliationMode.UpsertDesiredIdentities);
    }

    private static DbContextOptions<FullWorthDbContext>
        CreateOptions()
    {
        return new DbContextOptionsBuilder<
                FullWorthDbContext>()
            .UseInMemoryDatabase(
                $"bill-alert-gateway-{Guid.NewGuid():N}")
            .Options;
    }

    private static BillStreamEntity
        CreateBillStream(
            Guid userId)
    {
        return new BillStreamEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            ProviderName =
                "Midco",

            Category =
                BillCategory.Unknown,

            Source =
                BillStreamSource.Manual,

            IsActive =
                true,

            CreatedAtUtc =
                DateTimeOffset.UtcNow,

            UpdatedAtUtc =
                DateTimeOffset.UtcNow
        };
    }

    private static BillAlertEntity
        CreateAlert(
            Guid userId,
            Guid billStreamId,
            Guid? billChangeId,
            BillAlertType alertType,
            string title,
            string message,
            DateTimeOffset createdAtUtc)
    {
        return new BillAlertEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            BillStreamId =
                billStreamId,

            BillChangeId =
                billChangeId,

            AlertType =
                alertType,

            Severity =
                BillAlertSeverity.Warning,

            Title =
                title,

            Message =
                message,

            IsRead =
                false,

            IsDismissed =
                false,

            CreatedAtUtc =
                createdAtUtc,

            UpdatedAtUtc =
                createdAtUtc
        };
    }
}

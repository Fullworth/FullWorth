using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class AlertStagingReplayBatchTests
{
    [Theory]
    [InlineData(BillAlertReconciliationMode.ReplaceManagedSet, false)]
    [InlineData(BillAlertReconciliationMode.ReplaceManagedSet, true)]
    [InlineData(BillAlertReconciliationMode.SingleManagedSlot, false)]
    [InlineData(BillAlertReconciliationMode.SingleManagedSlot, true)]
    [InlineData(BillAlertReconciliationMode.UpsertDesiredIdentities, false)]
    [InlineData(BillAlertReconciliationMode.UpsertDesiredIdentities, true)]
    public async Task ClearThenRestageBeforeSave_UsesLiveDesiredState_WithoutDuplicatesOrForeignWrites(
        BillAlertReconciliationMode mode, bool pretracked)
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid(); var other = Guid.NewGuid();
        var bill = new BillStreamEntity { UserId = owner, ProviderName = "Owned" };
        var foreignBill = new BillStreamEntity { UserId = other, ProviderName = "Foreign" };
        var old = Alert(owner, bill.Id, BillAlertType.PaymentDue, "Old obligation");
        var fee = Alert(owner, bill.Id, BillAlertType.NewFee, "Keep fee");
        var foreign = Alert(other, foreignBill.Id, BillAlertType.PaymentDue, "Private foreign");
        db.AddRange(bill, foreignBill, old, fee, foreign); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        if (pretracked) await db.BillAlerts.Where(a => a.UserId == owner).LoadAsync();
        var gateway = new BillAlertReconciliationGateway(db);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await gateway.StageReconciliationAsync(owner, bill.Id,
                [new BillAlertReconciliationScope(null, [BillAlertContractType.PaymentDue], [],
                    BillAlertReconciliationMode.SingleManagedSlot)], [], DateTimeOffset.UtcNow);
            var desired = new BillAlertDesiredState(BillAlertContractType.PaymentDue,
                BillAlertContractSeverity.Warning, $"Fresh obligation {cycle}", "Current statement fact");
            var scope = new BillAlertReconciliationScope(null, [BillAlertContractType.PaymentDue], [desired], mode);
            await gateway.StageReconciliationAsync(owner, bill.Id, [scope], [], DateTimeOffset.UtcNow);
            var live = Assert.Single(db.ChangeTracker.Entries<BillAlertEntity>(),
                e => e.State != EntityState.Deleted && e.Entity.UserId == owner && e.Entity.AlertType == BillAlertType.PaymentDue);
            live.Entity.IsRead = live.Entity.IsDismissed = true;
            // Repeated identical staging must preserve acknowledgement and avoid another insert.
            await gateway.StageReconciliationAsync(owner, bill.Id, [scope], [], DateTimeOffset.UtcNow);
            Assert.True(live.Entity.IsRead);
            Assert.True(live.Entity.IsDismissed);
            Assert.Single(db.ChangeTracker.Entries<BillAlertEntity>(),
                e => e.State != EntityState.Deleted && e.Entity.UserId == owner && e.Entity.AlertType == BillAlertType.PaymentDue);
        }
        await using (var before = new FullWorthDbContext(options))
            Assert.Equal("Old obligation", (await before.BillAlerts.SingleAsync(a => a.Id == old.Id)).Title);
        await db.SaveChangesAsync();
        await using var stored = new FullWorthDbContext(options);
        var owned = await stored.BillAlerts.Where(a => a.UserId == owner && a.AlertType == BillAlertType.PaymentDue).ToListAsync();
        var latest = Assert.Single(owned);
        Assert.NotEqual(old.Id, latest.Id);
        Assert.Equal("Fresh obligation 1", latest.Title);
        Assert.True(latest.IsRead);
        Assert.True(latest.IsDismissed);
        Assert.Equal("Keep fee", (await stored.BillAlerts.SingleAsync(a => a.Id == fee.Id)).Title);
        Assert.Equal("Private foreign", (await stored.BillAlerts.SingleAsync(a => a.Id == foreign.Id)).Title);
    }

    [Fact]
    public async Task NewBillAlert_PendingDeletionDoesNotSuppressReplacement()
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var bill = new BillStreamEntity { UserId = owner, ProviderName = "Subscription", Source = BillStreamSource.AutomaticDiscovery };
        var old = Alert(owner, bill.Id, BillAlertType.NewBill, "Old discovery");
        db.AddRange(bill, old); await db.SaveChangesAsync();
        db.BillAlerts.Remove(old);
        var service = new RecurringBillDiscoveryAlertService(db);
        Assert.True(await service.EnsureNewBillAlertAsync(owner, bill, 3, DateTimeOffset.UtcNow));
        Assert.False(await service.EnsureNewBillAlertAsync(owner, bill, 3, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var alert = Assert.Single(await db.BillAlerts.ToListAsync());
        Assert.NotEqual(old.Id, alert.Id);
        Assert.Equal(BillAlertType.NewBill, alert.AlertType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanceledNewBillFastPath_NeverStagesOrReturnsFalse(bool alreadyTracked)
    {
        await using var db = Db();
        var owner = Guid.NewGuid();
        var bill = new BillStreamEntity { UserId = owner, ProviderName = "Subscription", Source = BillStreamSource.AutomaticDiscovery };
        db.Add(bill);
        if (alreadyTracked) db.Add(Alert(owner, bill.Id, BillAlertType.NewBill, "Pending"));
        var before = db.ChangeTracker.Entries().Select(e => e.Entity).ToArray();
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new RecurringBillDiscoveryAlertService(db).EnsureNewBillAlertAsync(owner, bill, 3,
                DateTimeOffset.UtcNow, cancellation.Token, newlyAddedStream: true));
        Assert.Equal(before, db.ChangeTracker.Entries().Select(e => e.Entity));
    }

    private static FullWorthDbContext Db() => new(new DbContextOptionsBuilder<FullWorthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static BillAlertEntity Alert(Guid owner, Guid bill, BillAlertType type, string title) => new()
    {
        UserId = owner, BillStreamId = bill, AlertType = type, Title = title, Message = title,
        Severity = BillAlertSeverity.Warning
    };
}

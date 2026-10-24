using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillEvidenceConcurrencyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Discovery_RejectsConcurrentManualConfirmation_AndClearsStagedChanges(bool active)
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase($"bill-concurrency-{Guid.NewGuid():N}").Options;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var sourceAccountId = Guid.NewGuid();
        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillStreams.Add(new BillStreamEntity { Id = id, UserId = owner,
                ProviderName = "Synthetic bill", Source = BillStreamSource.AutomaticDiscovery, IsActive = active });
            var transaction = new BankTransactionEntity { UserId = owner, BankAccountId = sourceAccountId,
                Name = "Synthetic bill", Amount = 10m,
                PostedDate = new DateOnly(2026, 1, 5), IsoCurrencyCode = "USD" };
            seed.BankTransactions.Add(transaction);
            // Deactivation now requires a sufficient owner-specific posted
            // history window. Preserve that prerequisite so this test still
            // exercises concurrent confirmation versus actual deactivation.
            seed.BankTransactions.Add(new BankTransactionEntity { UserId = owner,
                BankAccountId = sourceAccountId,
                Name = "Synthetic unrelated first", MerchantName = "Synthetic unrelated first",
                Amount = 3m, PostedDate = new DateOnly(2026, 1, 1), IsoCurrencyCode = "USD" });
            seed.BankTransactions.Add(new BankTransactionEntity { UserId = owner,
                BankAccountId = sourceAccountId,
                Name = "Synthetic unrelated last", MerchantName = "Synthetic unrelated last",
                Amount = 4m, PostedDate = new DateOnly(2026, 3, 8), IsoCurrencyCode = "USD" });
            seed.BillTransactionAssociations.Add(new BillTransactionAssociationEntity
                { UserId = owner, BillStreamId = id, BankTransactionId = transaction.Id });
            await seed.SaveChangesAsync();
        }
        await using var scan = new FullWorthDbContext(options);
        var gateway = new ConfirmDuringAssignmentGateway(scan, options, id);
        var service = new RecurringBillDiscoveryPersistenceService(scan,
            new PlaidBankTransactionDiscoveryGateway(scan), gateway);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => service.DiscoverAndSaveAsync(owner));
        Assert.False(scan.ChangeTracker.HasChanges());
        await using var verify = new FullWorthDbContext(options);
        var saved = await verify.BillStreams.SingleAsync();
        Assert.Equal(BillStreamSource.Manual, saved.Source);
        Assert.True(saved.IsActive);
    }

    [Fact]
    public async Task AssociationChanges_AfterStaging_CannotBeOverwritten()
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase($"association-concurrency-{Guid.NewGuid():N}").Options;
        var owner = Guid.NewGuid();
        var transaction = Guid.NewGuid();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();
        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillTransactionAssociations.Add(new BillTransactionAssociationEntity
                { UserId = owner, BankTransactionId = transaction, BillStreamId = first });
            seed.BillStreams.Add(new BillStreamEntity { UserId = owner, Id = second, ProviderName = "Synthetic" });
            await seed.SaveChangesAsync();
        }
        await using var stale = new FullWorthDbContext(options);
        await new BillTransactionAssociationGateway(stale).StageAssignmentsAsync(owner,
            [new(transaction, first, second)], DateTimeOffset.UtcNow);
        await using (var other = new FullWorthDbContext(options))
        {
            (await other.BillTransactionAssociations.SingleAsync()).BillStreamId = third;
            await other.SaveChangesAsync();
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
        await using var verify = new FullWorthDbContext(options);
        Assert.Equal(third, (await verify.BillTransactionAssociations.SingleAsync()).BillStreamId);
    }

    [Fact]
    public async Task StaleConfirmation_DetectsDiscoveryDeactivationInsteadOfSavingManualInactive()
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase($"confirmation-concurrency-{Guid.NewGuid():N}").Options;
        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillStreams.Add(new BillStreamEntity { UserId = Guid.NewGuid(), ProviderName = "Synthetic",
                Source = BillStreamSource.AutomaticDiscovery, IsActive = true });
            await seed.SaveChangesAsync();
        }
        await using var confirmation = new FullWorthDbContext(options);
        var stale = await confirmation.BillStreams.SingleAsync();
        await using (var scanner = new FullWorthDbContext(options))
        {
            (await scanner.BillStreams.SingleAsync()).IsActive = false;
            await scanner.SaveChangesAsync();
        }
        stale.Source = BillStreamSource.Manual;
        stale.IsActive = true;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => confirmation.SaveChangesAsync());
    }

    [Fact]
    public async Task AssignmentFailure_ClearsNewStreamsAndAlertsBeforeSecondaryWork()
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase($"failed-discovery-{Guid.NewGuid():N}").Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid();
        for (var month = 1; month <= 3; month++)
            db.BankTransactions.Add(new BankTransactionEntity { UserId = owner,
                Name = "Synthetic Subscription", Amount = 20m, IsoCurrencyCode = "USD",
                PostedDate = new DateOnly(2026, month, 5),
                CategoryPrimary = "GENERAL_MERCHANDISE",
                CategoryDetailed = "GENERAL_MERCHANDISE_DIGITAL_GOODS" });
        await db.SaveChangesAsync();
        var service = new RecurringBillDiscoveryPersistenceService(db,
            new PlaidBankTransactionDiscoveryGateway(db), new FailingAssignmentGateway());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DiscoverAndSaveAsync(owner));
        Assert.False(db.ChangeTracker.HasChanges());
        // A later scoped service may save unrelated reconciliation work.
        await db.SaveChangesAsync();
        Assert.Empty(db.BillStreams);
        Assert.Empty(db.BillAlerts);
        Assert.Empty(db.BillTransactionAssociations);
    }

    private sealed class FailingAssignmentGateway : IBillTransactionAssociationGateway
    {
        public Task<IReadOnlyList<BillTransactionAssociationRecord>> GetAsync(Guid userId,
            IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillTransactionAssociationRecord>>([]);
        public Task StageAssignmentsAsync(Guid userId,
            IReadOnlyCollection<BillTransactionAssociationAssignment> assignments, DateTimeOffset now,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Synthetic stale association batch");
    }

    private sealed class ConfirmDuringAssignmentGateway(FullWorthDbContext scan,
        DbContextOptions<FullWorthDbContext> options, Guid streamId) : IBillTransactionAssociationGateway
    {
        private readonly BillTransactionAssociationGateway _inner = new(scan);
        public Task<IReadOnlyList<BillTransactionAssociationRecord>> GetAsync(Guid userId,
            IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(userId, transactionIds, cancellationToken);
        public async Task StageAssignmentsAsync(Guid userId,
            IReadOnlyCollection<BillTransactionAssociationAssignment> assignments, DateTimeOffset now,
            CancellationToken cancellationToken = default)
        {
            await _inner.StageAssignmentsAsync(userId, assignments, now, cancellationToken);
            await using var confirmation = new FullWorthDbContext(options);
            var stream = await confirmation.BillStreams.SingleAsync(s => s.Id == streamId, cancellationToken);
            stream.Source = BillStreamSource.Manual;
            stream.IsActive = true;
            await confirmation.SaveChangesAsync(cancellationToken);
        }
    }
}

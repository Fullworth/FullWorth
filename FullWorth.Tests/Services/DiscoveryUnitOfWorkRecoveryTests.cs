using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using BillCategory = FullWorth.Core.Models.BillCategory;

namespace FullWorth.Tests.Services;

public sealed class DiscoveryUnitOfWorkRecoveryTests
{
    private static readonly DateTimeOffset OldTime = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var mode in new[] { "assignment", "save", "cancel", "concurrency" })
        foreach (var callerChanges in new[] { false, true })
        foreach (var pretrackedEvidence in new[] { false, true })
            yield return [mode, callerChanges, pretrackedEvidence];
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task FailedDiscovery_RestoresExistingUnitOfWork_AndRetriesWithoutPartialEvidence(
        string mode, bool callerChanges, bool pretrackedEvidence)
    {
        var failure = new Failure { Mode = mode };
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).AddInterceptors(failure).Options);
        var owner = Guid.NewGuid();
        var account = Guid.NewGuid();
        var existing = new BillStreamEntity
        {
            UserId = owner, ProviderName = "Fixture Existing Subscription",
            Source = BillStreamSource.AutomaticDiscovery, Category = BillCategory.Unknown,
            IsActive = false, UpdatedAtUtc = OldTime
        };
        var retired = new BillStreamEntity
        {
            UserId = owner, ProviderName = "Fixture Old Bill",
            Source = BillStreamSource.AutomaticDiscovery, Category = BillCategory.Other,
            IsActive = true, UpdatedAtUtc = OldTime
        };
        var foreign = new BillStreamEntity
        {
            UserId = Guid.NewGuid(), ProviderName = "Foreign manual bill",
            Source = BillStreamSource.Manual, IsActive = true, UpdatedAtUtc = OldTime
        };
        var caller = new BankConnectionEntity { UserId = owner, InstitutionName = "Original caller bank" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller deleted bank" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller added bank" };
        db.AddRange(existing, retired, foreign, caller, deleted);
        BankTransactionEntity Charge(string merchant, DateOnly date) => new()
        {
            UserId = owner, BankAccountId = account, Name = merchant, MerchantName = merchant,
            PlaidTransactionId = Guid.NewGuid().ToString("N"),
            PostedDate = date, Amount = 19m, IsoCurrencyCode = "USD",
            CategoryPrimary = "GENERAL_MERCHANDISE",
            CategoryDetailed = "GENERAL_MERCHANDISE_DIGITAL_GOODS"
        };
        BankTransactionEntity? firstExisting = null;
        for (var month = 1; month <= 3; month++)
        {
            var charge = Charge(existing.ProviderName, new DateOnly(2026, month, 5));
            firstExisting ??= charge;
            db.Add(charge);
            db.Add(Charge("Fixture New Subscription", new DateOnly(2026, month, 5)));
        }
        var oldCharge = Charge(retired.ProviderName, new DateOnly(2026, 1, 10));
        db.Add(oldCharge);
        db.Add(Charge("Unrelated first coverage charge", new DateOnly(2026, 1, 1)));
        db.Add(Charge("Unrelated last coverage charge", new DateOnly(2026, 3, 8)));
        db.BillTransactionAssociations.AddRange(
            new BillTransactionAssociationEntity
            {
                UserId = owner, BillStreamId = existing.Id,
                BankTransactionId = firstExisting!.Id, UpdatedAtUtc = OldTime
            },
            new BillTransactionAssociationEntity
            {
                UserId = owner, BillStreamId = retired.Id,
                BankTransactionId = oldCharge.Id, UpdatedAtUtc = OldTime
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        caller = await db.BankConnections.SingleAsync(c => c.Id == caller.Id);
        deleted = await db.BankConnections.SingleAsync(c => c.Id == deleted.Id);
        if (callerChanges)
        {
            caller.InstitutionName = "Unsaved caller bank";
            db.Remove(deleted);
            db.Add(added);
        }
        if (pretrackedEvidence)
        {
            await db.BillStreams.Where(s => s.UserId == owner).LoadAsync();
            await db.BillTransactionAssociations.Where(a => a.UserId == owner).LoadAsync();
            existing = db.BillStreams.Local.Single(s => s.Id == existing.Id);
            if (callerChanges) existing.UpdatedAtUtc = OldTime.AddDays(1);
        }
        var before = Capture(db);
        using var cancellation = new CancellationTokenSource();
        failure.Cancel = cancellation.Cancel;
        failure.Armed = true;
        var gateway = new FailingAssignments(db, failure);
        var service = new RecurringBillDiscoveryPersistenceService(db,
            new PlaidBankTransactionDiscoveryGateway(db), gateway);

        var error = await Record.ExceptionAsync(() =>
            service.DiscoverAndSaveAsync(owner, cancellation.Token));
        if (mode == "assignment") Assert.IsType<InvalidOperationException>(error);
        else if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (mode == "concurrency") Assert.IsType<DbUpdateConcurrencyException>(error);
        else Assert.IsType<DbUpdateException>(error);
        Assert.Equal(1, failure.Failures);
        Assert.True(failure.SawCompleteStagedBatch);
        Assert.Equal(before.Count, db.ChangeTracker.Entries().Count());
        foreach (var original in before)
        {
            var restored = Assert.Single(db.ChangeTracker.Entries(),
                e => ReferenceEquals(e.Entity, original.Entity));
            Assert.Equal(original.State, restored.State);
            Assert.Equal(original.Modified, restored.Properties.Where(p => p.IsModified)
                .Select(p => p.Metadata.Name).Order().ToArray());
            foreach (var (property, value) in original.Current)
                Assert.Equal(value, restored.CurrentValues[property]);
            foreach (var (property, value) in original.Original)
                Assert.Equal(value, restored.OriginalValues[property]);
        }

        // Secondary reconciliation can save the same context after discovery
        // failed. Only preexisting caller work may be persisted.
        await db.SaveChangesAsync();
        Assert.Equal(callerChanges ? "Unsaved caller bank" : "Original caller bank",
            (await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == caller.Id)).InstitutionName);
        Assert.Equal(!callerChanges, await db.BankConnections.AnyAsync(c => c.Id == deleted.Id));
        Assert.Equal(callerChanges, await db.BankConnections.AnyAsync(c => c.Id == added.Id));
        Assert.Equal(2, await db.BillStreams.CountAsync(s => s.UserId == owner));
        var storedExisting = await db.BillStreams.AsNoTracking().SingleAsync(s => s.Id == existing.Id);
        Assert.False(storedExisting.IsActive);
        Assert.Equal(BillCategory.Unknown, storedExisting.Category);
        Assert.Equal(pretrackedEvidence && callerChanges ? OldTime.AddDays(1) : OldTime, storedExisting.UpdatedAtUtc);
        var storedRetired = await db.BillStreams.AsNoTracking().SingleAsync(s => s.Id == retired.Id);
        Assert.True(storedRetired.IsActive);
        Assert.Equal(BillStreamSource.AutomaticDiscovery, storedRetired.Source);
        Assert.Equal(OldTime, storedRetired.UpdatedAtUtc);
        Assert.False(await db.BillAlerts.AnyAsync());
        var retainedLinks = await db.BillTransactionAssociations.AsNoTracking()
            .Where(a => a.UserId == owner).ToListAsync();
        Assert.Equal(2, retainedLinks.Count);
        Assert.Contains(retainedLinks, a => a.BankTransactionId == firstExisting!.Id && a.BillStreamId == existing.Id);
        Assert.Contains(retainedLinks, a => a.BankTransactionId == oldCharge.Id && a.BillStreamId == retired.Id);

        var result = await service.DiscoverAndSaveAsync(owner);
        Assert.Equal(1, result.BillStreamsCreated);
        Assert.Equal(1, result.BillStreamsUpdated);
        Assert.Equal(1, result.BillStreamsDeactivated);
        Assert.Equal(5, result.TransactionsLinked);
        Assert.Equal(1, result.TransactionsUnlinked);
        Assert.Equal(1, result.NewBillAlertsCreated);
        Assert.Equal(3, await db.BillStreams.CountAsync(s => s.UserId == owner));
        Assert.Equal(6, await db.BillTransactionAssociations.CountAsync(a => a.UserId == owner));
        Assert.Equal(1, await db.BillAlerts.CountAsync(a => a.UserId == owner && a.AlertType == BillAlertType.NewBill));
        var storedForeign = await db.BillStreams.AsNoTracking().SingleAsync(s => s.Id == foreign.Id);
        Assert.Equal(BillStreamSource.Manual, storedForeign.Source);
        Assert.True(storedForeign.IsActive);
        Assert.Equal(OldTime, storedForeign.UpdatedAtUtc);
    }

    private static List<TrackedState> Capture(FullWorthDbContext db) =>
        db.ChangeTracker.Entries().Select(e => new TrackedState(e.Entity, e.State,
            e.CurrentValues.Properties.ToDictionary(p => p.Name, p => e.CurrentValues[p]),
            e.OriginalValues.Properties.ToDictionary(p => p.Name, p => e.OriginalValues[p]),
            e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray())).ToList();

    private sealed record TrackedState(object Entity, EntityState State,
        Dictionary<string, object?> Current, Dictionary<string, object?> Original, string[] Modified);

    private sealed class FailingAssignments(FullWorthDbContext db, Failure failure)
        : IBillTransactionAssociationGateway
    {
        private readonly BillTransactionAssociationGateway _inner = new(db);
        public Task<IReadOnlyList<BillTransactionAssociationRecord>> GetAsync(Guid owner,
            IReadOnlyCollection<Guid> ids, CancellationToken token = default) => _inner.GetAsync(owner, ids, token);
        public async Task StageAssignmentsAsync(Guid owner,
            IReadOnlyCollection<BillTransactionAssociationAssignment> assignments,
            DateTimeOffset now, CancellationToken token = default)
        {
            await _inner.StageAssignmentsAsync(owner, assignments, now, token);
            if (failure.Mode == "assignment") failure.ThrowIfArmed(db, token);
        }
    }

    private sealed class Failure : SaveChangesInterceptor
    {
        public required string Mode { get; init; }
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }
        public bool SawCompleteStagedBatch { get; private set; }

        public void ThrowIfArmed(FullWorthDbContext db, CancellationToken token)
        {
            if (!Armed) return;
            Armed = false;
            Failures++;
            SawCompleteStagedBatch =
                db.ChangeTracker.Entries<BillStreamEntity>().Any(e => e.State == EntityState.Added) &&
                db.ChangeTracker.Entries<BillStreamEntity>().Any(e =>
                    e.Entity.ProviderName == "Fixture Existing Subscription" && e.Entity.IsActive) &&
                db.ChangeTracker.Entries<BillStreamEntity>().Any(e =>
                    e.Entity.ProviderName == "Fixture Old Bill" && !e.Entity.IsActive) &&
                db.ChangeTracker.Entries<BillAlertEntity>().Any(e => e.State == EntityState.Added) &&
                db.ChangeTracker.Entries<BillTransactionAssociationEntity>().Any(e => e.State == EntityState.Added) &&
                db.ChangeTracker.Entries<BillTransactionAssociationEntity>().Any(e => e.State == EntityState.Deleted);
            if (Mode == "cancel")
            {
                Cancel!();
                token.ThrowIfCancellationRequested();
            }
            if (Mode == "assignment") throw new InvalidOperationException("Synthetic staged assignment failure.");
            if (Mode == "concurrency") throw new DbUpdateConcurrencyException("Synthetic pre-commit concurrency failure.");
            throw new DbUpdateException("Synthetic pre-commit discovery failure.");
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken token = default)
        {
            if (Mode != "assignment") ThrowIfArmed((FullWorthDbContext)eventData.Context!, token);
            return ValueTask.FromResult(result);
        }
    }
}

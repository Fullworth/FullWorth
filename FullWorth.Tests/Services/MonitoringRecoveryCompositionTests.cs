using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Plaid;
using FullWorth.API.Services.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace FullWorth.Tests.Services;

public sealed class MonitoringRecoveryCompositionTests
{
    [Theory]
    [InlineData("discovery", false)]
    [InlineData("discovery", true)]
    [InlineData("planning", false)]
    [InlineData("planning", true)]
    public async Task SecondaryHealthSave_DoesNotCommitFailedDiscoveryOrPlan_AndRetryWorks(
        string phase, bool unexpected)
    {
        var failure = new Failure(phase, unexpected);
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var caller = new BankConnectionEntity { UserId = owner, InstitutionName = "Original caller bank" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller deleted bank" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller added bank" };
        var attention = new BankConnectionEntity
        {
            UserId = owner, InstitutionName = "Synthetic attention bank", Status = BankConnectionStatus.RequiresAttention
        };
        var foreign = new BankConnectionEntity
        {
            UserId = other, InstitutionName = "Foreign bank", Status = BankConnectionStatus.RequiresAttention
        };
        var manual = new BillStreamEntity { UserId = owner, ProviderName = "Manual bill", Source = BillStreamSource.Manual };
        db.AddRange(caller, deleted, attention, foreign, manual);
        if (phase == "discovery")
        {
            var account = Guid.NewGuid();
            for (var month = 1; month <= 3; month++)
                db.BankTransactions.Add(new BankTransactionEntity
                {
                    UserId = owner, BankAccountId = account, PlaidTransactionId = $"synthetic-{month}",
                    Name = "Fixture Digital Subscription", MerchantName = "Fixture Digital Subscription",
                    PostedDate = new DateOnly(2026, month, 5), Amount = 19m, IsoCurrencyCode = "USD",
                    CategoryPrimary = "GENERAL_MERCHANDISE", CategoryDetailed = "GENERAL_MERCHANDISE_DIGITAL_GOODS"
                });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        caller = await db.BankConnections.SingleAsync(c => c.Id == caller.Id);
        deleted = await db.BankConnections.SingleAsync(c => c.Id == deleted.Id);
        var staged = false;
        void StageCallerWork()
        {
            if (staged) return;
            staged = true;
            caller.InstitutionName = "Pending caller bank";
            db.Remove(deleted);
            db.Add(added);
        }
        if (phase == "discovery") StageCallerWork();
        var planning = new Planning(db, owner, manual.Id, phase == "planning" ? StageCallerWork : null);
        var refresh = new BillMonitoringRefreshService(new NoSync(),
            new RecurringBillDiscoveryPersistenceService(db, new PlaidBankTransactionDiscoveryGateway(db),
                new BillTransactionAssociationGateway(db)),
            new BankConnectionHealthAlertService(db, new PlaidBankConnectionReadGateway(db)),
            planning, NullLogger<BillMonitoringRefreshService>.Instance);
        failure.Armed = true;

        var error = await Record.ExceptionAsync(() => refresh.RefreshAsync(owner));
        if (phase == "discovery") Assert.Same(failure.Error, error);
        else Assert.Null(error); // Planning remains secondary to a successful core refresh.
        Assert.Equal(1, failure.Failures);
        Assert.True(failure.SawFinancialCandidates);

        await using (var stored = new FullWorthDbContext(options))
        {
            Assert.Equal("Pending caller bank", (await stored.BankConnections.SingleAsync(c => c.Id == caller.Id)).InstitutionName);
            Assert.False(await stored.BankConnections.AnyAsync(c => c.Id == deleted.Id));
            Assert.True(await stored.BankConnections.AnyAsync(c => c.Id == added.Id));
            Assert.Equal("Foreign bank", (await stored.BankConnections.SingleAsync(c => c.Id == foreign.Id)).InstitutionName);
            Assert.Equal(BankConnectionStatus.RequiresAttention,
                (await stored.BankConnections.SingleAsync(c => c.Id == foreign.Id)).Status);
            Assert.Equal(1, await stored.BillStreams.CountAsync());
            Assert.Equal(BillStreamSource.Manual, (await stored.BillStreams.SingleAsync()).Source);
            Assert.False(await stored.BillTransactionAssociations.AnyAsync());
            Assert.False(await stored.PlanningPaycheckPlanRuns.AnyAsync());
            Assert.False(await stored.PlanningPaycheckAllocations.AnyAsync());
            var health = Assert.Single(await stored.BillAlerts.ToListAsync());
            Assert.Equal(owner, health.UserId);
            Assert.Equal(BillAlertType.ConnectionIssue, health.AlertType);
            Assert.DoesNotContain("Foreign bank", health.Message);
        }

        await refresh.RefreshAsync(owner);
        // Persist once more to detect candidates left staged by the first attempt.
        await db.SaveChangesAsync();
        await using var final = new FullWorthDbContext(options);
        Assert.Equal(1, await final.BillAlerts.CountAsync(a => a.UserId == owner && a.AlertType == BillAlertType.ConnectionIssue));
        Assert.False(await final.BillAlerts.AnyAsync(a => a.UserId == other));
        if (phase == "discovery")
        {
            Assert.Equal(2, await final.BillStreams.CountAsync());
            Assert.Equal(3, await final.BillTransactionAssociations.CountAsync());
            Assert.Equal(1, await final.BillAlerts.CountAsync(a => a.AlertType == BillAlertType.NewBill));
            Assert.False(await final.PlanningPaycheckPlanRuns.AnyAsync());
        }
        else
        {
            var run = Assert.Single(await final.PlanningPaycheckPlanRuns.ToListAsync());
            var allocation = Assert.Single(await final.PlanningPaycheckAllocations.ToListAsync());
            Assert.Equal(owner, run.UserId);
            Assert.Equal(owner, allocation.UserId);
            Assert.Equal(250m, run.PaycheckAmount);
            Assert.Equal(25m, run.RecommendedSetAside);
            Assert.Equal(225m, run.PaycheckRemainingAfterPlan);
            Assert.Equal(25m, allocation.PlannedAmount);
            Assert.Equal(manual.Id, allocation.BillStreamId);
            await refresh.RefreshAsync(owner); // Existing immutable result is reused.
            Assert.Equal(1, await final.PlanningPaycheckPlanRuns.CountAsync());
            Assert.Equal(1, await final.PlanningPaycheckAllocations.CountAsync());
        }
    }

    private sealed class NoSync : IBankDataSyncGateway
    {
        public Task SyncAccountsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SyncTransactionsAsync(Guid userId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class Planning(FullWorthDbContext db, Guid owner, Guid bill, Action? stage)
        : IPlanningPaydayAlertRefreshGateway
    {
        private readonly PlanningPaycheckAllocationStore _store = new(db, TimeProvider.System);
        private readonly Guid _payroll = Guid.NewGuid();
        private readonly Guid _statement = Guid.NewGuid();
        public async Task<PlanningPaydayAlertRefreshResult> RefreshAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(owner, userId);
            if (stage is not null)
            {
                stage();
                await _store.SaveCompletePaycheckPlanAsync(userId, _payroll,
                    new PlanningPaycheckPlanRunDraft(new DateOnly(2026, 10, 1), 250m, "USD", 25m, 225m, 0m),
                    [new PlanningPaycheckAllocationDraft(bill, _statement, new DateOnly(2026, 10, 1),
                        new DateOnly(2026, 10, 15), 25m, "USD")], cancellationToken);
            }
            return new PlanningPaydayAlertRefreshResult(0, 0, 0, false);
        }
    }

    private sealed class Failure(string phase, bool unexpected) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Failures { get; private set; }
        public bool SawFinancialCandidates { get; private set; }
        public Exception Error { get; } = unexpected
            ? new InvalidOperationException("Synthetic unexpected pre-commit failure.")
            : new DbUpdateException("Synthetic database pre-commit failure.");
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = (FullWorthDbContext)eventData.Context!;
            var staged = phase == "discovery"
                ? db.ChangeTracker.Entries<BillStreamEntity>().Any(e => e.State == EntityState.Added && e.Entity.Source == BillStreamSource.AutomaticDiscovery)
                    && db.ChangeTracker.Entries<BillTransactionAssociationEntity>().Any(e => e.State == EntityState.Added)
                    && db.ChangeTracker.Entries<BillAlertEntity>().Any(e => e.State == EntityState.Added && e.Entity.AlertType == BillAlertType.NewBill)
                : db.ChangeTracker.Entries<PlanningPaycheckPlanRunEntity>().Any(e => e.State == EntityState.Added)
                    && db.ChangeTracker.Entries<PlanningPaycheckAllocationEntity>().Any(e => e.State == EntityState.Added);
            if (Armed && staged)
            {
                Armed = false;
                Failures++;
                SawFinancialCandidates = true;
                throw Error;
            }
            return ValueTask.FromResult(result);
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PaydayReplayHistoricalNamesTests
{
    [Theory]
    [InlineData("active", "Synthetic Utilities")]
    [InlineData("inactive", "Synthetic Utilities")]
    [InlineData("renamed", "Renamed Utilities")]
    [InlineData("deleted", null)]
    [InlineData("foreign", null)]
    public async Task Replay_UsesSavedOwnedIdentities_WithoutChangingFrozenFacts(string state, string? expectedName)
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid();
        var bill = new BillStreamEntity
        {
            UserId = state == "foreign" ? Guid.NewGuid() : owner,
            ProviderName = state == "renamed" ? "Renamed Utilities" : "Synthetic Utilities",
            IsActive = state != "inactive", Source = BillStreamSource.Manual, Category = BillCategory.Utility
        };
        db.BillStreams.Add(bill);
        await db.SaveChangesAsync();
        var payroll = Guid.NewGuid();
        var store = new PlanningPaycheckAllocationStore(db, TimeProvider.System);
        var coverage = new PlanningPaycheckPlanCoverage(1, 0, 0, 0, 0, 0);
        var saved = await store.SaveCompletePaycheckPlanAsync(owner, payroll,
            new PlanningPaycheckPlanRunDraft(new DateOnly(2026, 9, 25), 1000m, "USD", 125m, 875m, 0m)
            { Coverage = coverage },
            [new PlanningPaycheckAllocationDraft(bill.Id, Guid.NewGuid(), new DateOnly(2026, 10, 15),
                new DateOnly(2026, 10, 23), 125m, "USD")]);
        if (state == "deleted")
        {
            db.BillStreams.Remove(bill);
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        var bills = new HistoricalOnlyGateway(new BillStreamReadGateway(db));
        var service = new PlanningPaydayPlanService(new PlanningSettingsService(db, bills, TimeProvider.System),
            store, bills, new NoLiveFacts(), new NoLivePayroll());

        var result = await service.GenerateAsync(owner, payroll, new DateOnly(2026, 9, 25));

        Assert.True(result.IsReplay);
        var item = Assert.Single(result.Items);
        Assert.Equal(expectedName, item.ProviderName);
        Assert.Equal(bill.Id, item.BillStreamId);
        Assert.Equal(125m, item.PlannedAmount);
        Assert.Equal(coverage, result.Coverage);
        Assert.Equal(saved.Run.RecommendedSetAside, result.RecommendedSetAside);
        Assert.Equal(saved.Run.PaycheckRemainingAfterPlan, result.PaycheckRemainingAfterPlan);
        Assert.Equal(saved.Run.Shortfall, result.Shortfall);
        Assert.Equal(owner, bills.RequestedOwner);
        Assert.Equal(new[] { bill.Id }, bills.RequestedIds);
        Assert.False(db.ChangeTracker.HasChanges());
        var after = await store.GetSavedPaycheckPlanAsync(owner, payroll);
        Assert.NotNull(after);
        Assert.Equal(saved.Run, after.Run);
        Assert.Equal(saved.Allocations, after.Allocations);
        if (state != "deleted")
            Assert.Equal(state != "inactive", (await db.BillStreams.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task EmptySavedPlan_ReplaysWithoutAnyBillLookup()
    {
        await using var db = CreateDb();
        var owner = Guid.NewGuid();
        var payroll = Guid.NewGuid();
        var store = new PlanningPaycheckAllocationStore(db, TimeProvider.System);
        await store.SaveCompletePaycheckPlanAsync(owner, payroll,
            new PlanningPaycheckPlanRunDraft(new DateOnly(2026, 9, 25), 1000m, "USD", 0m, 1000m, 0m), []);
        var bills = new HistoricalOnlyGateway(new BillStreamReadGateway(db));
        var service = new PlanningPaydayPlanService(new PlanningSettingsService(db, bills, TimeProvider.System),
            store, bills, new NoLiveFacts(), new NoLivePayroll());
        var result = await service.GenerateAsync(owner, payroll, new DateOnly(2026, 9, 25));
        Assert.True(result.IsReplay);
        Assert.Empty(result.Items);
        Assert.Null(bills.RequestedIds);
        Assert.Null(result.Coverage);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static FullWorthDbContext CreateDb() => new(new DbContextOptionsBuilder<FullWorthDbContext>()
        .UseInMemoryDatabase($"payday-replay-names-{Guid.NewGuid():N}").Options);

    private sealed class HistoricalOnlyGateway(IBillStreamReadGateway inner) : IBillStreamReadGateway
    {
        public Guid? RequestedOwner { get; private set; }
        public Guid[]? RequestedIds { get; private set; }
        public Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Replay must not load today's active bill list.");
        public Task<BillStreamReadRecord?> GetOwnedAsync(Guid userId, Guid billStreamId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Replay must use the bounded batch lookup.");
        public Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedByIdsAsync(Guid userId,
            IReadOnlyCollection<Guid> billStreamIds, CancellationToken cancellationToken = default)
        {
            RequestedOwner = userId;
            RequestedIds = billStreamIds.ToArray();
            return inner.ListOwnedByIdsAsync(userId, billStreamIds, cancellationToken);
        }
    }

    private sealed class NoLiveFacts : IBillPlanningFactsGateway
    {
        public Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(Guid userId,
            IReadOnlyCollection<Guid> billStreamIds, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Replay must not reload current statement facts.");
    }

    private sealed class NoLivePayroll : IPlanningPostedPayrollFactsGateway
    {
        public Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(Guid userId, DateOnly fromInclusive,
            DateOnly throughInclusive, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Replay must not reload corrected/removed payroll.");
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models.Planning;
using FullWorth.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class LegacySemiMonthlyScheduleRecoveryTests
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(0, false)]
    public void Calculator_RejectsIncompleteSemiMonthlyInput_IncludingEarlyReturnPaths(int amount, bool alreadyDue)
    {
        var date = new DateOnly(2026, 10, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => PaycheckBillPlanCalculator.Calculate(
            new PaycheckBillPlanRequest(amount, 0m, alreadyDue ? date : date.AddDays(30), date, 2,
                new PayScheduleDefinition(PayScheduleFrequency.SemiMonthly, date))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyIncompleteSchedule_RequiresReconfiguration_WithoutPersistingEvenAnEmptyPlan(bool hasBill)
    {
        await using var db = Db();
        var owner = Guid.NewGuid(); var other = Guid.NewGuid(); var payroll = Guid.NewGuid();
        var bill = new BillStreamEntity { UserId = owner, ProviderName = "Owned internet", IsActive = true };
        db.Add(Schedule(owner, PayScheduleFrequency.SemiMonthly));
        db.Add(Schedule(other, PayScheduleFrequency.Monthly));
        if (hasBill) db.Add(bill);
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var billReads = new BillStreamReadGateway(db);
        var settings = new PlanningSettingsService(db, billReads, TimeProvider.System);
        var facts = new Facts(owner, bill.Id);
        var store = new PlanningPaycheckAllocationStore(db, TimeProvider.System);
        var service = new PlanningPaydayPlanService(settings, store, billReads, facts, new Payroll(owner, payroll));
        var blocked = await service.GenerateAsync(owner, payroll, new DateOnly(2026, 10, 1));
        Assert.Equal(PlanningPaydayPlanStatus.PayScheduleRequired, blocked.Status);
        Assert.Empty(blocked.Items);
        Assert.Equal(0, facts.Calls);
        Assert.False(await db.PlanningPaycheckPlanRuns.AnyAsync());
        Assert.False(await db.PlanningPaycheckAllocations.AnyAsync());
        Assert.Null((await db.PlanningPaySchedules.SingleAsync(s => s.UserId == owner)).SecondaryDayOfMonth);
        Assert.Equal(PayScheduleFrequency.Monthly, (await db.PlanningPaySchedules.SingleAsync(s => s.UserId == other)).Frequency);

        await settings.SavePayScheduleAsync(owner, PayScheduleFrequency.SemiMonthly, new DateOnly(2026, 10, 1), 15, 2);
        var repaired = await service.GenerateAsync(owner, payroll, new DateOnly(2026, 10, 1));
        Assert.Equal(PlanningPaydayPlanStatus.Ready, repaired.Status);
        Assert.Equal(hasBill ? 300m : 0m, repaired.RecommendedSetAside);
        Assert.Equal(hasBill ? 700m : 1000m, repaired.PaycheckRemainingAfterPlan);
        Assert.Equal(1, await db.PlanningPaycheckPlanRuns.CountAsync());
        Assert.Equal(hasBill ? 1 : 0, await db.PlanningPaycheckAllocations.CountAsync());
        Assert.Equal(hasBill ? 1 : 0, facts.Calls);
    }

    [Fact]
    public async Task HistoricalReplay_PreservesFrozenAmountsDespiteIncompleteCurrentSchedule()
    {
        await using var db = Db();
        var owner = Guid.NewGuid(); var payroll = Guid.NewGuid();
        var bill = new BillStreamEntity { UserId = owner, ProviderName = "Historical internet", IsActive = false };
        db.AddRange(bill, Schedule(owner, PayScheduleFrequency.SemiMonthly)); await db.SaveChangesAsync();
        var reads = new BillStreamReadGateway(db);
        var store = new PlanningPaycheckAllocationStore(db, TimeProvider.System);
        var statement = Guid.NewGuid();
        await store.SaveCompletePaycheckPlanAsync(owner, payroll,
            new PlanningPaycheckPlanRunDraft(new DateOnly(2026, 10, 1), 1000m, "USD", 25m, 975m, 0m),
            [new PlanningPaycheckAllocationDraft(bill.Id, statement, new DateOnly(2026, 9, 30),
                new DateOnly(2026, 10, 31), 25m, "USD")]);
        var facts = new Facts(owner, bill.Id);
        var service = new PlanningPaydayPlanService(new PlanningSettingsService(db, reads, TimeProvider.System),
            store, reads, facts, new Payroll(owner, payroll, forbidRead: true));
        var replay = await service.GenerateAsync(owner, payroll, new DateOnly(2026, 10, 1));
        Assert.Equal(PlanningPaydayPlanStatus.Ready, replay.Status);
        Assert.True(replay.IsReplay);
        Assert.Equal(25m, replay.RecommendedSetAside);
        Assert.Equal(975m, replay.PaycheckRemainingAfterPlan);
        Assert.Equal(statement, Assert.Single(replay.Items).SourceStatementId);
        Assert.Equal(0, facts.Calls);
        Assert.Equal(1, await db.PlanningPaycheckPlanRuns.CountAsync());
        Assert.Null((await db.PlanningPaySchedules.SingleAsync()).SecondaryDayOfMonth);
    }

    private static FullWorthDbContext Db() => new(new DbContextOptionsBuilder<FullWorthDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static PlanningPayScheduleEntity Schedule(Guid owner, PayScheduleFrequency frequency) => new()
    {
        UserId = owner, Frequency = frequency, AnchorPayDate = new DateOnly(2026, 10, 1),
        DefaultPaychecksAhead = 2, SecondaryDayOfMonth = null
    };
    private sealed class Payroll(Guid owner, Guid payroll, bool forbidRead = false) : IPlanningPostedPayrollFactsGateway
    {
        public Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(Guid userId, DateOnly fromInclusive,
            DateOnly throughInclusive, CancellationToken cancellationToken = default)
        {
            Assert.False(forbidRead);
            Assert.Equal(owner, userId);
            return Task.FromResult<IReadOnlyList<PlanningPostedPayrollFact>>(
                [new PlanningPostedPayrollFact(payroll, 1000m, new DateOnly(2026, 10, 1), "USD")]);
        }
    }
    private sealed class Facts(Guid owner, Guid bill) : IBillPlanningFactsGateway
    {
        public int Calls { get; private set; }
        public Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(Guid userId,
            IReadOnlyCollection<Guid> billStreamIds, CancellationToken cancellationToken = default)
        {
            Assert.Equal(owner, userId); Calls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, BillPlanningFact>>(
                new Dictionary<Guid, BillPlanningFact>
                {
                    [bill] = new(bill, Guid.NewGuid(), 600m, "USD", new DateOnly(2026, 10, 31), new DateOnly(2026, 9, 30))
                });
        }
    }
}

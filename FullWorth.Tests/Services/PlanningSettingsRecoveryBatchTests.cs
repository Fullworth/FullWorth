using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FullWorth.Tests.Services;

public sealed class PlanningSettingsRecoveryBatchTests
{
    private static readonly DateTimeOffset Old = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    public static IEnumerable<object[]> Cases()
    {
        foreach (var operation in new[] { "schedule-create", "schedule-update", "preference-create", "preference-update", "preference-delete" })
        foreach (var failure in new[] { "database", "cancel", "unexpected" })
        foreach (var callerChanges in new[] { false, true })
            yield return [operation, failure, callerChanges];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task FailedSettingsWrite_PreservesCallerState_DoesNotLeak_AndRetries(
        string operation, string mode, bool callerChanges)
    {
        var failure = new Failure(mode);
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options;
        await using var db = new FullWorthDbContext(options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var bill = new BillStreamEntity { UserId = owner, ProviderName = "Owned bill", Source = BillStreamSource.Manual };
        var foreignBill = new BillStreamEntity { UserId = other, ProviderName = "Foreign bill", Source = BillStreamSource.Manual };
        var schedule = Schedule(owner);
        var foreignSchedule = Schedule(other);
        var preference = Preference(owner, bill.Id);
        var foreignPreference = Preference(other, foreignBill.Id);
        var caller = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller original" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller deleted" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Caller added" };
        db.AddRange(bill, foreignBill, foreignSchedule, foreignPreference, caller, deleted);
        var existingSchedule = operation == "schedule-update";
        var existingPreference = operation is "preference-update" or "preference-delete";
        if (existingSchedule) db.Add(schedule);
        if (existingPreference) db.Add(preference);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        if (callerChanges)
        {
            caller = await db.BankConnections.SingleAsync(c => c.Id == caller.Id);
            caller.InstitutionName = "Caller pending";
            db.Remove(await db.BankConnections.SingleAsync(c => c.Id == deleted.Id));
            db.Add(added);
            if (existingSchedule)
            {
                schedule = await db.PlanningPaySchedules.SingleAsync(s => s.Id == schedule.Id);
                schedule.UpdatedAtUtc = Old.AddDays(1);
            }
            if (existingPreference)
            {
                preference = await db.PlanningBillFundingPreferences.SingleAsync(p => p.Id == preference.Id);
                preference.UpdatedAtUtc = Old.AddDays(1);
            }
        }
        var before = db.ChangeTracker.Entries().Select(e => new Tracked(e.Entity, e.State,
            e.CurrentValues.Clone(), e.OriginalValues.Clone(),
            e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray())).ToArray();
        using var cancellation = new CancellationTokenSource();
        failure.Cancel = cancellation.Cancel;
        failure.Armed = true;
        var service = new PlanningSettingsService(db, new BillStreamReadGateway(db), TimeProvider.System);
        async Task Write(CancellationToken token = default)
        {
            if (operation.StartsWith("schedule", StringComparison.Ordinal))
                await service.SavePayScheduleAsync(owner, PayScheduleFrequency.SemiMonthly, new DateOnly(2026, 10, 1), 15, 5, token);
            else if (operation == "preference-delete")
                Assert.True(await service.DeleteBillFundingPreferenceAsync(owner, bill.Id, token));
            else Assert.True(await service.SaveBillFundingPreferenceAsync(owner, bill.Id, 5, token));
        }
        var error = await Record.ExceptionAsync(() => Write(cancellation.Token));
        Assert.NotNull(error);
        if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (mode == "database") Assert.IsType<DbUpdateException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(1, failure.Failures);
        foreach (var baseline in before)
        {
            var entry = db.Entry(baseline.Entity);
            Assert.Equal(baseline.State, entry.State);
            Assert.Equal(baseline.Modified, entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order());
            foreach (var property in baseline.Current.Properties)
            {
                Assert.Equal(baseline.Current[property], entry.CurrentValues[property]);
                Assert.Equal(baseline.Original[property], entry.OriginalValues[property]);
            }
        }
        Assert.DoesNotContain(db.ChangeTracker.Entries<PlanningPayScheduleEntity>(),
            e => e.State == EntityState.Added && e.Entity.UserId == owner);
        Assert.DoesNotContain(db.ChangeTracker.Entries<PlanningBillFundingPreferenceEntity>(),
            e => e.State == EntityState.Added && e.Entity.UserId == owner);

        // Saving other work must not persist the failed preference/schedule operation.
        await db.SaveChangesAsync();
        await using (var stored = new FullWorthDbContext(options))
        {
            var s = await stored.PlanningPaySchedules.SingleOrDefaultAsync(s => s.UserId == owner);
            var p = await stored.PlanningBillFundingPreferences.SingleOrDefaultAsync(p => p.UserId == owner);
            Assert.Equal(existingSchedule, s is not null);
            Assert.Equal(existingPreference, p is not null);
            if (s is not null)
            {
                Assert.Equal(PayScheduleFrequency.Monthly, s.Frequency);
                Assert.Equal(2, s.DefaultPaychecksAhead);
                Assert.Null(s.SecondaryDayOfMonth);
                Assert.Equal(callerChanges ? Old.AddDays(1) : Old, s.UpdatedAtUtc);
            }
            if (p is not null)
            {
                Assert.Equal(2, p.PaychecksAheadOverride);
                Assert.Equal(callerChanges ? Old.AddDays(1) : Old, p.UpdatedAtUtc);
            }
            Assert.Equal(callerChanges ? "Caller pending" : "Caller original",
                (await stored.BankConnections.SingleAsync(c => c.Id == caller.Id)).InstitutionName);
            Assert.Equal(callerChanges, await stored.BankConnections.AnyAsync(c => c.Id == added.Id));
            Assert.Equal(!callerChanges, await stored.BankConnections.AnyAsync(c => c.Id == deleted.Id));
            Assert.Equal(Old, (await stored.PlanningPaySchedules.SingleAsync(s => s.UserId == other)).UpdatedAtUtc);
            Assert.Equal(2, (await stored.PlanningBillFundingPreferences.SingleAsync(p => p.UserId == other)).PaychecksAheadOverride);
        }

        await Write();
        await using var final = new FullWorthDbContext(options);
        if (operation.StartsWith("schedule", StringComparison.Ordinal))
        {
            var s = await final.PlanningPaySchedules.SingleAsync(s => s.UserId == owner);
            Assert.Equal(PayScheduleFrequency.SemiMonthly, s.Frequency);
            Assert.Equal(15, s.SecondaryDayOfMonth);
            Assert.Equal(5, s.DefaultPaychecksAhead);
        }
        else if (operation == "preference-delete")
            Assert.False(await final.PlanningBillFundingPreferences.AnyAsync(p => p.UserId == owner));
        else Assert.Equal(5, (await final.PlanningBillFundingPreferences.SingleAsync(p => p.UserId == owner)).PaychecksAheadOverride);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignPreferenceWriteOrDelete_ReturnsFalseWithoutSavingCallerWork(bool delete)
    {
        var failure = new Failure("database");
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(failure).Options);
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var foreignBill = new BillStreamEntity { UserId = other, ProviderName = "Foreign" };
        db.Add(foreignBill); db.Add(Preference(other, foreignBill.Id)); await db.SaveChangesAsync();
        var pending = new BankConnectionEntity { UserId = owner, InstitutionName = "Pending" }; db.Add(pending);
        failure.Armed = true;
        var service = new PlanningSettingsService(db, new BillStreamReadGateway(db), TimeProvider.System);
        Assert.False(delete ? await service.DeleteBillFundingPreferenceAsync(owner, foreignBill.Id)
            : await service.SaveBillFundingPreferenceAsync(owner, foreignBill.Id, 5));
        Assert.Equal(0, failure.Failures);
        Assert.Equal(EntityState.Added, db.Entry(pending).State);
        Assert.Equal(2, (await db.PlanningBillFundingPreferences.SingleAsync()).PaychecksAheadOverride);
    }

    private static PlanningPayScheduleEntity Schedule(Guid owner) => new()
    {
        UserId = owner, Frequency = PayScheduleFrequency.Monthly, AnchorPayDate = new DateOnly(2026, 9, 1),
        DefaultPaychecksAhead = 2, CreatedAtUtc = Old, UpdatedAtUtc = Old
    };
    private static PlanningBillFundingPreferenceEntity Preference(Guid owner, Guid bill) => new()
    {
        UserId = owner, BillStreamId = bill, PaychecksAheadOverride = 2, CreatedAtUtc = Old, UpdatedAtUtc = Old
    };
    private sealed record Tracked(object Entity, EntityState State,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues Current,
        Microsoft.EntityFrameworkCore.ChangeTracking.PropertyValues Original, string[] Modified);
    private sealed class Failure(string mode) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Failures { get; private set; }
        public Action? Cancel { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false; Failures++;
                if (mode == "cancel") { Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
                if (mode == "unexpected") throw new InvalidOperationException("Synthetic pre-commit failure.");
                throw new DbUpdateException("Synthetic pre-commit failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

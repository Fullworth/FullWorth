using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace FullWorth.Tests.Services;

public sealed class PaydayAlertFailureIsolationTests
{
    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var mode in new[] { "save", "cancel", "unexpected", "race-identical", "race-conflict" })
        foreach (var callerChanges in new[] { false, true })
            yield return [mode, callerChanges];
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task FailedAlertCandidate_PreservesCallerChanges_AndStrictRaceSemantics(
        string mode, bool callerChanges)
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var plain = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(name, root).Options;
        var failure = new AlertFailure { PlainOptions = plain, Mode = mode };
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(name, root).AddInterceptors(failure).Options);
        var owner = Guid.NewGuid();
        var source = Guid.NewGuid();
        var now = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var request = new PlanningPaydayAlertRequest(source, PlanningPaydayAlertSeverity.Info,
            "Fixture payday plan", "Immutable fixture alert content");
        var modified = new BankConnectionEntity { UserId = owner, InstitutionName = "Original bank" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Deleted caller bank" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Added caller bank" };
        var foreign = Alert(Guid.NewGuid(), request, now);
        foreign.Title = "Foreign original alert";
        foreign.IsRead = true;
        foreign.IsDismissed = true;
        db.AddRange(modified, deleted, foreign);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        modified = await db.BankConnections.SingleAsync(c => c.Id == modified.Id);
        deleted = await db.BankConnections.SingleAsync(c => c.Id == deleted.Id);
        if (callerChanges)
        {
            modified.InstitutionName = "Unsaved caller bank";
            db.Remove(deleted);
            db.Add(added);
        }
        db.ChangeTracker.DetectChanges();
        var before = db.ChangeTracker.Entries<BankConnectionEntity>()
            .ToDictionary(e => e.Entity.Id, e => e.State);
        var modifiedFlags = db.Entry(modified).Properties.Where(p => p.IsModified)
            .Select(p => p.Metadata.Name).Order().ToArray();

        if (mode.StartsWith("race-", StringComparison.Ordinal))
        {
            failure.Winner = Alert(owner, request, now);
            failure.Winner.IsRead = true;
            failure.Winner.IsDismissed = true;
            if (mode == "race-conflict") failure.Winner.Message = "Different immutable winner";
        }
        using var cancellation = new CancellationTokenSource();
        failure.Cancel = cancellation.Cancel;
        failure.Armed = true;
        var gateway = new PlanningPaydayAlertGateway(db);
        var error = await Record.ExceptionAsync(() =>
            gateway.EnsureAsync(owner, request, now, cancellation.Token));
        if (mode == "race-identical") Assert.Null(error);
        else if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (mode is "unexpected" or "race-conflict") Assert.IsType<InvalidOperationException>(error);
        else Assert.IsType<DbUpdateException>(error);
        Assert.Equal(1, failure.Failures);
        Assert.DoesNotContain(db.ChangeTracker.Entries<BillAlertEntity>(),
            e => e.State == EntityState.Added && e.Entity.UserId == owner && e.Entity.SourceEventId == source);
        Assert.Equal(before.Count, db.ChangeTracker.Entries<BankConnectionEntity>().Count());
        foreach (var (id, state) in before)
            Assert.Equal(state, db.ChangeTracker.Entries<BankConnectionEntity>().Single(e => e.Entity.Id == id).State);
        Assert.Same(modified, db.ChangeTracker.Entries<BankConnectionEntity>()
            .Single(e => e.Entity.Id == modified.Id).Entity);
        Assert.Equal(modifiedFlags, db.Entry(modified).Properties.Where(p => p.IsModified)
            .Select(p => p.Metadata.Name).Order().ToArray());
        Assert.Equal("Original bank", db.Entry(modified).OriginalValues.GetValue<string>(nameof(modified.InstitutionName)));

        // This models a later monitoring/connection-health save in the shared
        // context. It must retain caller work without persisting a failed alert.
        await db.SaveChangesAsync();
        Assert.Equal(callerChanges ? "Unsaved caller bank" : "Original bank",
            (await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == modified.Id)).InstitutionName);
        Assert.Equal(!callerChanges, await db.BankConnections.AnyAsync(c => c.Id == deleted.Id));
        Assert.Equal(callerChanges, await db.BankConnections.AnyAsync(c => c.Id == added.Id));
        var stored = await db.BillAlerts.AsNoTracking()
            .SingleOrDefaultAsync(a => a.UserId == owner && a.SourceEventId == source);
        if (mode.StartsWith("race-", StringComparison.Ordinal))
        {
            Assert.NotNull(stored);
            Assert.True(stored.IsRead);
            Assert.True(stored.IsDismissed);
            Assert.Equal(now, stored.CreatedAtUtc);
            Assert.Equal(now, stored.UpdatedAtUtc);
            Assert.Equal(mode == "race-conflict" ? "Different immutable winner" : request.Message, stored.Message);
        }
        else Assert.Null(stored);

        if (mode == "race-conflict")
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                gateway.EnsureAsync(owner, request, now.AddHours(1)));
        else
        {
            await gateway.EnsureAsync(owner, request, now.AddHours(1));
            Assert.Equal(1, await db.BillAlerts.CountAsync(a =>
                a.UserId == owner && a.AlertType == BillAlertType.PaydayPlan && a.SourceEventId == source));
        }
        var storedForeign = await db.BillAlerts.AsNoTracking().SingleAsync(a => a.Id == foreign.Id);
        Assert.Equal("Foreign original alert", storedForeign.Title);
        Assert.True(storedForeign.IsRead);
        Assert.True(storedForeign.IsDismissed);
        Assert.Equal(now, storedForeign.UpdatedAtUtc);
    }

    private static BillAlertEntity Alert(Guid owner, PlanningPaydayAlertRequest request, DateTimeOffset now) => new()
    {
        UserId = owner, SourceEventId = request.SourceEventId,
        AlertType = BillAlertType.PaydayPlan, Severity = BillAlertSeverity.Info,
        Title = request.Title, Message = request.Message,
        CreatedAtUtc = now, UpdatedAtUtc = now
    };

    private sealed class AlertFailure : SaveChangesInterceptor
    {
        public required DbContextOptions<FullWorthDbContext> PlainOptions { get; init; }
        public required string Mode { get; init; }
        public bool Armed { get; set; }
        public int Failures { get; private set; }
        public Action? Cancel { get; set; }
        public BillAlertEntity? Winner { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return result;
            Armed = false;
            Failures++;
            if (Winner is not null)
            {
                await using var other = new FullWorthDbContext(PlainOptions);
                other.Add(Winner);
                await other.SaveChangesAsync(CancellationToken.None);
            }
            if (Mode == "cancel")
            {
                Cancel!();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (Mode == "unexpected") throw new InvalidOperationException("Synthetic pre-commit failure.");
            throw new DbUpdateException("Synthetic failed/competing alert insert.");
        }
    }
}

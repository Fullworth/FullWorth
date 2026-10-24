using FullWorth.API.Data;
using FullWorth.Core.Formatting;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FullWorth.API.Services.Statements;

/// <summary>
/// Re-evaluates an existing PaymentDue slot using current UTC date and the
/// latest owner-scoped statement. It never invents payment state or due facts.
/// </summary>
public sealed class BillStatementDueAlertMaintenance(
    FullWorthDbContext db,
    IBillDueReminderMaintenanceReadGateway billReads,
    BillStatementPaymentDueAlertService dueAlerts)
{
    public async Task ReconcileOneAsync(
        Guid userId, Guid billStreamId, DateOnly today, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || billStreamId == Guid.Empty)
            throw new ArgumentException("A valid owner and bill identity are required.");

        var isActive = await billReads.IsOwnedActiveAsync(
            userId, billStreamId, cancellationToken);

        // A missing/foreign stream must not cause an alert write or leak data.
        if (isActive is null)
            return;

        var latest = await db.BillStatements.AsNoTracking()
            .Where(item => item.UserId == userId && item.BillStreamId == billStreamId)
            .OrderByDescending(item => item.PeriodEnd)
            .ThenByDescending(item => item.RetrievedAtUtc)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .Select(item => new
            {
                item.DueDate, item.TotalAmount, item.CurrencyCode
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Statements owns this scoped unit of work. Capture tracker metadata,
        // leaving Bills-owned alert reads/writes inside the owner contract.
        var snapshots = db.ChangeTracker.Entries().Select(entry => new ReminderSnapshot(entry)).ToArray();
        var baseline = snapshots.Select(snapshot => snapshot.Entity).ToHashSet();

        try
        {
            // Invalid or absent latest evidence cannot support an asserted
            // payable obligation. Do not borrow an older statement or invent USD.
            if (!isActive.Value || latest is null || !latest.DueDate.HasValue ||
                latest.DueDate.Value < today || latest.TotalAmount <= 0m ||
                !FinancialAmountFormatter.TryNormalizeCurrency(latest.CurrencyCode, out _))
            {
                await dueAlerts.ClearAsync(userId, billStreamId, now, cancellationToken);
            }
            else
            {
                await dueAlerts.ReconcileAsync(userId, billStreamId, latest.DueDate,
                    latest.TotalAmount, latest.CurrencyCode, today, now, cancellationToken);
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Only entities first tracked by this failed reconciliation are
            // discarded; preexisting caller work retains exact metadata.
            foreach (var entry in db.ChangeTracker.Entries()
                .Where(entry => !baseline.Contains(entry.Entity)).ToArray())
                entry.State = EntityState.Detached;
            foreach (var snapshot in snapshots) snapshot.Restore();
            throw;
        }
    }

    private sealed class ReminderSnapshot(EntityEntry entry)
    {
        public object Entity { get; } = entry.Entity;
        private readonly PropertyValues _current = entry.CurrentValues.Clone();
        private readonly PropertyValues _original = entry.OriginalValues.Clone();
        private readonly EntityState _state = entry.State;
        private readonly HashSet<string> _modified = entry.Properties
            .Where(property => property.IsModified).Select(property => property.Metadata.Name).ToHashSet();

        public void Restore()
        {
            entry.State = EntityState.Unchanged;
            entry.CurrentValues.SetValues(_current);
            entry.OriginalValues.SetValues(_original);
            entry.State = _state;
            if (_state is EntityState.Modified or EntityState.Unchanged)
                foreach (var property in entry.Properties)
                    property.IsModified = _modified.Contains(property.Metadata.Name);
        }
    }
}

/// <summary>
/// Sweep at UTC-date boundaries without requiring another provider upload.
/// Each owned bill is processed in a fresh scope to isolate EF tracking and
/// avoid one failure blocking every other customer's reminders.
/// </summary>
public sealed class BillStatementDueAlertMaintenanceWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<BillStatementDueAlertMaintenanceWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    "Payment reminder maintenance could not complete: {ExceptionType}.",
                    ex.GetType().Name);
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<BillDueReminderMaintenanceCandidate> candidates;
        using (var scope = scopeFactory.CreateScope())
        {
            var billReads = scope.ServiceProvider
                .GetRequiredService<IBillDueReminderMaintenanceReadGateway>();
            candidates = await billReads.GetCandidatesAsync(cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // A separate scoped DbContext for each alert prevents one failed
            // unit of work or its tracked data from contaminating another.
            using var scope = scopeFactory.CreateScope();
            try
            {
                var maintenance = scope.ServiceProvider
                    .GetRequiredService<BillStatementDueAlertMaintenance>();
                await maintenance.ReconcileOneAsync(
                    candidate.UserId, candidate.BillStreamId,
                    today, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // No provider, user, amount, document text, or account identifiers.
                logger.LogWarning(
                    "A payment reminder maintenance item failed: {ExceptionType}.",
                    ex.GetType().Name);
            }
        }
    }
}

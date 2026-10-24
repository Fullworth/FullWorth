using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FullWorth.API.Services.Bills;

public sealed class BankConnectionHealthAlertService
{
    private const string AlertTitle =
        "Bank connection needs attention";

    private readonly FullWorthDbContext
        _dbContext;

    private readonly IBankConnectionReadGateway
        _bankConnectionReadGateway;

    public BankConnectionHealthAlertService(
        FullWorthDbContext dbContext,
        IBankConnectionReadGateway bankConnectionReadGateway)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        ArgumentNullException.ThrowIfNull(
            bankConnectionReadGateway);

        _dbContext =
            dbContext;

        _bankConnectionReadGateway =
            bankConnectionReadGateway;
    }

    public async Task ReconcileAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        /*
         * Query only the rows and column needed to build the aggregate
         * connection-health alert. Active and intentionally disconnected
         * connections never need to be materialized here.
         */
        var attentionInstitutionNames =
            await _bankConnectionReadGateway
                .GetAttentionInstitutionNamesAsync(
                    userId,
                    cancellationToken);

        var persistedAlerts =
            await _dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            null &&
                        alert.BillChangeId ==
                            null &&
                        alert.AlertType ==
                            BillAlertType.ConnectionIssue &&
                        alert.Title ==
                            AlertTitle)
                .OrderBy(
                    alert =>
                        alert.CreatedAtUtc)
                .ThenBy(
                    alert =>
                        alert.Id)
                .ToListAsync(
                    cancellationToken);

        // Reconciliation must observe this unit of work as well as persisted
        // rows. Deleted alerts no longer occupy a live slot; an Added alert
        // already does. Never create a second candidate for the same slot.
        var trackedAlerts = _dbContext.ChangeTracker.Entries<BillAlertEntity>()
            .Where(entry => entry.Entity.UserId == userId &&
                entry.Entity.BillStreamId is null && entry.Entity.BillChangeId is null &&
                entry.Entity.AlertType == BillAlertType.ConnectionIssue &&
                entry.Entity.Title == AlertTitle)
            .Select(entry => entry.Entity);
        var existingAlerts = persistedAlerts.Concat(trackedAlerts)
            .DistinctBy(alert => alert.Id)
            .Where(alert => _dbContext.Entry(alert).State != EntityState.Deleted &&
                alert.UserId == userId && alert.BillStreamId is null && alert.BillChangeId is null &&
                alert.AlertType == BillAlertType.ConnectionIssue && alert.Title == AlertTitle)
            .OrderBy(alert => alert.CreatedAtUtc).ThenBy(alert => alert.Id)
            .ToList();

        _dbContext.ChangeTracker.DetectChanges();
        var snapshots = existingAlerts.Select(alert => new AlertSnapshot(_dbContext.Entry(alert))).ToArray();
        BillAlertEntity? candidate = null;

        try
        {
            if (attentionInstitutionNames.Count == 0)
            {
                if (existingAlerts.Count > 0)
                {
                    var needsSave = existingAlerts.Any(alert =>
                        _dbContext.Entry(alert).State != EntityState.Added);
                    _dbContext.BillAlerts.RemoveRange(existingAlerts);
                    if (needsSave)
                        await _dbContext.SaveChangesAsync(cancellationToken);
                }
                return;
            }

            var message = BuildMessage(attentionInstitutionNames);
            var now = DateTimeOffset.UtcNow;

            if (existingAlerts.Count == 0)
            {
                candidate = new BillAlertEntity
                {
                    UserId = userId,
                    AlertType = BillAlertType.ConnectionIssue,
                    Severity = BillAlertSeverity.Warning,
                    Title = AlertTitle,
                    Message = message,
                    IsRead = false,
                    IsDismissed = false,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };
                _dbContext.BillAlerts.Add(candidate);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var primaryAlert = existingAlerts[0];
            var changed = primaryAlert.Severity != BillAlertSeverity.Warning ||
                !string.Equals(primaryAlert.Message, message, StringComparison.Ordinal);

            if (changed)
            {
                primaryAlert.Severity = BillAlertSeverity.Warning;
                primaryAlert.Message = message;
                // A materially changed connection set should surface the corrected alert again.
                primaryAlert.IsRead = false;
                primaryAlert.IsDismissed = false;
                primaryAlert.UpdatedAtUtc = now;
            }

            if (existingAlerts.Count > 1)
            {
                _dbContext.BillAlerts.RemoveRange(existingAlerts.Skip(1));
            }

            // A no-op reconciliation must not save unrelated caller changes.
            if (changed || existingAlerts.Count > 1)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Remove only our unsaved insertion and restore only alerts we mutated.
            // Keep caller-owned pending work and its original/modified values intact.
            if (candidate is not null)
            {
                _dbContext.Entry(candidate).State = EntityState.Detached;
            }
            foreach (var snapshot in snapshots)
            {
                snapshot.Restore();
            }
            throw;
        }
    }

    private sealed class AlertSnapshot(EntityEntry<BillAlertEntity> entry)
    {
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
            if (_state is EntityState.Unchanged or EntityState.Modified)
            {
                foreach (var property in entry.Properties)
                {
                    property.IsModified = _modified.Contains(property.Metadata.Name);
                }
            }
        }
    }

    private static string BuildMessage(
        IReadOnlyList<string> institutionNames)
    {
        var names =
            institutionNames
                .Where(
                    name =>
                        !string.IsNullOrWhiteSpace(
                            name))
                .Select(
                    name =>
                        name.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .OrderBy(
                    name =>
                        name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (names.Count ==
            0)
        {
            return
                "A connected bank needs attention before FullWorth can continue reliable bank monitoring. Review the connection in Connect.";
        }

        if (names.Count ==
            1)
        {
            return
                $"{names[0]} needs attention before FullWorth can continue reliable bank monitoring. Review or reconnect it in Connect.";
        }

        var visibleNames =
            names
                .Take(
                    5)
                .ToList();

        var remainder =
            names.Count -
            visibleNames.Count;

        var remainderText =
            remainder >
            0
                ? $" and {remainder} more"
                : string.Empty;

        return
            $"{names.Count} bank connections need attention: {string.Join(", ", visibleNames)}{remainderText}. Review them in Connect so FullWorth can continue reliable monitoring.";
    }
}
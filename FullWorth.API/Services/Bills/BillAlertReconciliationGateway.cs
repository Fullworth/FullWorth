using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class BillAlertReconciliationGateway(
    FullWorthDbContext dbContext)
    : IBillAlertReconciliationGateway
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    public async Task StageReconciliationAsync(
        Guid userId,
        Guid billStreamId,
        IReadOnlyCollection<BillAlertReconciliationScope> scopes,
        IReadOnlyCollection<Guid> removeBillChangeIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(
            userId,
            billStreamId);

        ArgumentNullException.ThrowIfNull(
            scopes);

        ArgumentNullException.ThrowIfNull(
            removeBillChangeIds);

        cancellationToken.ThrowIfCancellationRequested();

        var ownedStreamExists =
            await dbContext.BillStreams
                .AsNoTracking()
                .AnyAsync(
                    stream =>
                        stream.Id ==
                            billStreamId &&
                        stream.UserId ==
                            userId,
                    cancellationToken);

        if (!ownedStreamExists)
        {
            throw new InvalidOperationException(
                "The owned bill stream could not be found.");
        }

        var removals =
            new HashSet<Guid>();

        foreach (var billChangeId in
                 removeBillChangeIds)
        {
            if (billChangeId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "Bill change IDs scheduled for alert removal must not be empty.",
                    nameof(removeBillChangeIds));
            }

            removals.Add(
                billChangeId);
        }

        var normalizedScopes =
            new List<NormalizedScope>(
                scopes.Count);

        var claimedScopeTypes =
            new HashSet<
                ScopeTypeKey>();

        foreach (var scope in
                 scopes)
        {
            ArgumentNullException.ThrowIfNull(
                scope);

            if (scope.BillChangeId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "Bill change IDs must not be empty.",
                    nameof(scopes));
            }

            if (scope.BillChangeId.HasValue &&
                removals.Contains(
                    scope.BillChangeId.Value))
            {
                throw new InvalidOperationException(
                    "A bill change cannot be reconciled and removed in the same alert batch.");
            }

            ArgumentNullException.ThrowIfNull(
                scope.ManagedAlertTypes);

            ArgumentNullException.ThrowIfNull(
                scope.DesiredAlerts);

            if (scope.ManagedAlertTypes.Count ==
                0)
            {
                throw new ArgumentException(
                    "Each alert reconciliation scope must manage at least one alert type.",
                    nameof(scopes));
            }

            var managedTypes =
                new HashSet<BillAlertType>();

            foreach (var contractType in
                     scope.ManagedAlertTypes)
            {
                var entityType =
                    ToEntityType(
                        contractType);

                if (!managedTypes.Add(
                        entityType))
                {
                    throw new ArgumentException(
                        "Managed alert types must be unique within a scope.",
                        nameof(scopes));
                }

                if (!claimedScopeTypes.Add(
                        new ScopeTypeKey(
                            scope.BillChangeId,
                            entityType)))
                {
                    throw new ArgumentException(
                        "Alert reconciliation scopes must not overlap for the same bill change and alert type.",
                        nameof(scopes));
                }
            }

            var desired =
                new List<NormalizedDesiredAlert>(
                    scope.DesiredAlerts.Count);

            var desiredIdentities =
                new HashSet<AlertIdentity>();

            foreach (var desiredAlert in
                     scope.DesiredAlerts)
            {
                ArgumentNullException.ThrowIfNull(
                    desiredAlert);

                var entityType =
                    ToEntityType(
                        desiredAlert.AlertType);

                if (!managedTypes.Contains(
                        entityType))
                {
                    throw new ArgumentException(
                        "Desired alerts must belong to a type managed by their reconciliation scope.",
                        nameof(scopes));
                }

                var title =
                    desiredAlert.Title?.Trim()
                    ?? string.Empty;

                var message =
                    desiredAlert.Message?.Trim()
                    ?? string.Empty;

                if (title.Length ==
                    0)
                {
                    throw new ArgumentException(
                        "Desired alert titles are required.",
                        nameof(scopes));
                }

                if (message.Length ==
                    0)
                {
                    throw new ArgumentException(
                        "Desired alert messages are required.",
                        nameof(scopes));
                }

                if (title.Length >
                    MaxTitleLength)
                {
                    throw new ArgumentException(
                        $"Desired alert titles must not exceed {MaxTitleLength} characters.",
                        nameof(scopes));
                }

                if (message.Length >
                    MaxMessageLength)
                {
                    throw new ArgumentException(
                        $"Desired alert messages must not exceed {MaxMessageLength} characters.",
                        nameof(scopes));
                }

                var identity =
                    new AlertIdentity(
                        entityType,
                        title);

                if (!desiredIdentities.Add(
                        identity))
                {
                    throw new ArgumentException(
                        "Desired alert identities must be unique within a scope.",
                        nameof(scopes));
                }

                desired.Add(
                    new NormalizedDesiredAlert(
                        entityType,
                        ToEntitySeverity(
                            desiredAlert.Severity),
                        title,
                        message));
            }

            if (scope.Mode is not
                    BillAlertReconciliationMode.ReplaceManagedSet and not
                    BillAlertReconciliationMode.SingleManagedSlot and not
                    BillAlertReconciliationMode.UpsertDesiredIdentities)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(scopes),
                    scope.Mode,
                    "Unsupported alert reconciliation mode.");
            }

            if (scope.Mode ==
                    BillAlertReconciliationMode.SingleManagedSlot &&
                desired.Count >
                    1)
            {
                throw new ArgumentException(
                    "Single-slot alert reconciliation accepts at most one desired alert.",
                    nameof(scopes));
            }

            normalizedScopes.Add(
                new NormalizedScope(
                    scope.BillChangeId,
                    managedTypes,
                    desired,
                    scope.Mode));
        }

        if (normalizedScopes.Count ==
                0 &&
            removals.Count ==
                0)
        {
            return;
        }

        var targetedChangeIds =
            normalizedScopes
                .Where(
                    scope =>
                        scope.BillChangeId
                            .HasValue)
                .Select(
                    scope =>
                        scope.BillChangeId!
                            .Value)
                .Concat(
                    removals)
                .Distinct()
                .ToArray();

        var includeStreamLevelAlerts =
            normalizedScopes.Any(
                scope =>
                    !scope.BillChangeId
                        .HasValue);

        var persistedAlerts =
            await dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            billStreamId &&
                        (
                            (
                                alert.BillChangeId.HasValue &&
                                targetedChangeIds.Contains(
                                    alert.BillChangeId.Value)
                            ) ||
                            (
                                !alert.BillChangeId.HasValue &&
                                includeStreamLevelAlerts
                            )
                        ))
                .ToListAsync(
                    cancellationToken);

        var trackedAlerts =
            dbContext.BillAlerts.Local
                .Where(
                    alert =>
                        dbContext.Entry(
                                alert)
                            .State !=
                            EntityState.Deleted &&
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            billStreamId &&
                        (
                            (
                                alert.BillChangeId.HasValue &&
                                targetedChangeIds.Contains(
                                    alert.BillChangeId.Value)
                            ) ||
                            (
                                !alert.BillChangeId.HasValue &&
                                includeStreamLevelAlerts
                            )
                        ))
                .ToList();

        var alerts =
            persistedAlerts
                .Concat(
                    trackedAlerts)
                .DistinctBy(
                    alert =>
                        alert.Id)
                .ToList();

        var removedAlertIds =
            new HashSet<Guid>();

        foreach (var alert in
                 alerts.Where(
                     alert =>
                         alert.BillChangeId.HasValue &&
                         removals.Contains(
                             alert.BillChangeId.Value)))
        {
            if (removedAlertIds.Add(
                    alert.Id))
            {
                dbContext.BillAlerts.Remove(
                    alert);
            }
        }

        foreach (var scope in
                 normalizedScopes)
        {
            var existing =
                alerts
                    .Where(
                        alert =>
                            !removedAlertIds.Contains(
                                alert.Id) &&
                            alert.BillChangeId ==
                                scope.BillChangeId &&
                            scope.ManagedTypes.Contains(
                                alert.AlertType))
                    .OrderBy(
                        alert =>
                            alert.CreatedAtUtc)
                    .ThenBy(
                        alert =>
                            alert.Id)
                    .ToList();

            switch (scope.Mode)
            {
                case BillAlertReconciliationMode.ReplaceManagedSet:
                    ReconcileReplaceManagedSet(
                        userId,
                        billStreamId,
                        scope,
                        existing,
                        now,
                        removedAlertIds);
                    break;

                case BillAlertReconciliationMode.SingleManagedSlot:
                    ReconcileSingleManagedSlot(
                        userId,
                        billStreamId,
                        scope,
                        existing,
                        now,
                        removedAlertIds);
                    break;

                case BillAlertReconciliationMode.UpsertDesiredIdentities:
                    ReconcileUpsertDesiredIdentities(
                        userId,
                        billStreamId,
                        scope,
                        existing,
                        now,
                        removedAlertIds);
                    break;

                default:
                    throw new InvalidOperationException(
                        "The normalized alert reconciliation mode is unsupported.");
            }
        }

        /*
         * Intentionally do not SaveChanges here.
         *
         * Statements and Bills currently share one scoped modular-monolith
         * unit of work. Statement rows, Bill Changes, and Bills-owned alerts
         * must commit together.
         */
    }

    private void ReconcileReplaceManagedSet(
        Guid userId,
        Guid billStreamId,
        NormalizedScope scope,
        IReadOnlyList<BillAlertEntity> existing,
        DateTimeOffset now,
        ISet<Guid> removedAlertIds)
    {
        var existingByIdentity =
            new Dictionary<
                AlertIdentity,
                BillAlertEntity>();

        foreach (var alert in
                 existing)
        {
            var identity =
                new AlertIdentity(
                    alert.AlertType,
                    alert.Title);

            if (existingByIdentity.TryAdd(
                    identity,
                    alert))
            {
                continue;
            }

            RemoveAlert(
                alert,
                removedAlertIds);
        }

        var desiredIdentities =
            new HashSet<AlertIdentity>();

        foreach (var desired in
                 scope.Desired)
        {
            var identity =
                new AlertIdentity(
                    desired.AlertType,
                    desired.Title);

            desiredIdentities.Add(
                identity);

            if (!existingByIdentity.TryGetValue(
                    identity,
                    out var existingAlert))
            {
                AddAlert(
                    userId,
                    billStreamId,
                    scope.BillChangeId,
                    desired,
                    now);

                continue;
            }

            ApplyDesiredContent(
                existingAlert,
                desired,
                now,
                allowIdentityChange:
                    false);
        }

        foreach (var pair in
                 existingByIdentity)
        {
            if (desiredIdentities.Contains(
                    pair.Key))
            {
                continue;
            }

            RemoveAlert(
                pair.Value,
                removedAlertIds);
        }
    }

    private void ReconcileSingleManagedSlot(
        Guid userId,
        Guid billStreamId,
        NormalizedScope scope,
        IReadOnlyList<BillAlertEntity> existing,
        DateTimeOffset now,
        ISet<Guid> removedAlertIds)
    {
        var desired =
            scope.Desired
                .SingleOrDefault();

        if (desired is null)
        {
            foreach (var alert in
                     existing)
            {
                RemoveAlert(
                    alert,
                    removedAlertIds);
            }

            return;
        }

        if (existing.Count ==
            0)
        {
            AddAlert(
                userId,
                billStreamId,
                scope.BillChangeId,
                desired,
                now);

            return;
        }

        ApplyDesiredContent(
            existing[0],
            desired,
            now,
            allowIdentityChange:
                true);

        foreach (var duplicate in
                 existing.Skip(
                     1))
        {
            RemoveAlert(
                duplicate,
                removedAlertIds);
        }
    }

    private void ReconcileUpsertDesiredIdentities(
        Guid userId,
        Guid billStreamId,
        NormalizedScope scope,
        IReadOnlyList<BillAlertEntity> existing,
        DateTimeOffset now,
        ISet<Guid> removedAlertIds)
    {
        foreach (var desired in
                 scope.Desired)
        {
            var matching =
                existing
                    .Where(
                        alert =>
                            alert.AlertType ==
                                desired.AlertType &&
                            string.Equals(
                                alert.Title,
                                desired.Title,
                                StringComparison.Ordinal))
                    .ToList();

            if (matching.Count ==
                0)
            {
                AddAlert(
                    userId,
                    billStreamId,
                    scope.BillChangeId,
                    desired,
                    now);

                continue;
            }

            ApplyDesiredContent(
                matching[0],
                desired,
                now,
                allowIdentityChange:
                    false);

            foreach (var duplicate in
                     matching.Skip(
                         1))
            {
                RemoveAlert(
                    duplicate,
                    removedAlertIds);
            }
        }
    }

    private void AddAlert(
        Guid userId,
        Guid billStreamId,
        Guid? billChangeId,
        NormalizedDesiredAlert desired,
        DateTimeOffset now)
    {
        dbContext.BillAlerts.Add(
            new BillAlertEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStreamId,

                BillChangeId =
                    billChangeId,

                AlertType =
                    desired.AlertType,

                Severity =
                    desired.Severity,

                Title =
                    desired.Title,

                Message =
                    desired.Message,

                IsRead =
                    false,

                IsDismissed =
                    false,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            });
    }

    private static void ApplyDesiredContent(
        BillAlertEntity alert,
        NormalizedDesiredAlert desired,
        DateTimeOffset now,
        bool allowIdentityChange)
    {
        var changed =
            alert.Severity !=
                desired.Severity ||
            !string.Equals(
                alert.Message,
                desired.Message,
                StringComparison.Ordinal) ||
            (
                allowIdentityChange &&
                (
                    alert.AlertType !=
                        desired.AlertType ||
                    !string.Equals(
                        alert.Title,
                        desired.Title,
                        StringComparison.Ordinal)
                )
            );

        if (!changed)
        {
            return;
        }

        if (allowIdentityChange)
        {
            alert.AlertType =
                desired.AlertType;

            alert.Title =
                desired.Title;
        }

        alert.Severity =
            desired.Severity;

        alert.Message =
            desired.Message;

        alert.IsRead =
            false;

        alert.IsDismissed =
            false;

        alert.UpdatedAtUtc =
            now;
    }

    private void RemoveAlert(
        BillAlertEntity alert,
        ISet<Guid> removedAlertIds)
    {
        if (!removedAlertIds.Add(
                alert.Id))
        {
            return;
        }

        dbContext.BillAlerts.Remove(
            alert);
    }

    private static void ValidateIdentity(
        Guid userId,
        Guid billStreamId)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (billStreamId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }
    }

    private static BillAlertType ToEntityType(
        BillAlertContractType alertType)
    {
        return alertType switch
        {
            BillAlertContractType.BillIncrease =>
                BillAlertType.BillIncrease,

            BillAlertContractType.BillDecrease =>
                BillAlertType.BillDecrease,

            BillAlertContractType.NewFee =>
                BillAlertType.NewFee,

            BillAlertContractType.RemovedDiscount =>
                BillAlertType.RemovedDiscount,

            BillAlertContractType.PaymentDue =>
                BillAlertType.PaymentDue,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(alertType),
                    alertType,
                    "Unsupported statement alert type.")
        };
    }

    private static BillAlertSeverity ToEntitySeverity(
        BillAlertContractSeverity severity)
    {
        return severity switch
        {
            BillAlertContractSeverity.Info =>
                BillAlertSeverity.Info,

            BillAlertContractSeverity.Warning =>
                BillAlertSeverity.Warning,

            BillAlertContractSeverity.Critical =>
                BillAlertSeverity.Critical,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(severity),
                    severity,
                    "Unsupported statement alert severity.")
        };
    }

    private readonly record struct ScopeTypeKey(
        Guid? BillChangeId,
        BillAlertType AlertType);

    private readonly record struct AlertIdentity(
        BillAlertType AlertType,
        string Title);

    private sealed record NormalizedScope(
        Guid? BillChangeId,
        IReadOnlySet<BillAlertType> ManagedTypes,
        IReadOnlyList<NormalizedDesiredAlert> Desired,
        BillAlertReconciliationMode Mode);

    private sealed record NormalizedDesiredAlert(
        BillAlertType AlertType,
        BillAlertSeverity Severity,
        string Title,
        string Message);
}

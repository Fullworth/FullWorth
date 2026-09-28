using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class PlanningPaydayAlertGateway(
    FullWorthDbContext dbContext)
    : IPlanningPaydayAlertGateway
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    public async Task EnsureAsync(
        Guid userId,
        PlanningPaydayAlertRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            request);

        if (request.SourceEventId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Source event ID is required.",
                nameof(request));
        }

        var normalized =
            Normalize(
                request);

        var existing =
            await FindAsync(
                userId,
                normalized.SourceEventId,
                cancellationToken);

        if (existing is not null)
        {
            EnsureIdentical(
                existing,
                normalized);

            return;
        }

        dbContext.BillAlerts.Add(
            new BillAlertEntity
            {
                UserId =
                    userId,
                BillStreamId =
                    null,
                BillChangeId =
                    null,
                SourceEventId =
                    normalized.SourceEventId,
                AlertType =
                    BillAlertType.PaydayPlan,
                Severity =
                    ToEntitySeverity(
                        normalized.Severity),
                Title =
                    normalized.Title,
                Message =
                    normalized.Message,
                IsRead =
                    false,
                IsDismissed =
                    false,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            });

        try
        {
            await dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch (DbUpdateException)
        {
            /*
             * The unique (UserId, AlertType, SourceEventId) index is the
             * concurrency boundary. If a competing request created the same
             * immutable alert first, accept it only when the content matches.
             */
            dbContext.ChangeTracker.Clear();

            existing =
                await FindAsync(
                    userId,
                    normalized.SourceEventId,
                    cancellationToken);

            if (existing is not null)
            {
                EnsureIdentical(
                    existing,
                    normalized);

                return;
            }

            throw;
        }
    }

    private async Task<BillAlertEntity?> FindAsync(
        Guid userId,
        Guid sourceEventId,
        CancellationToken cancellationToken)
    {
        return await dbContext.BillAlerts
            .SingleOrDefaultAsync(
                alert =>
                    alert.UserId ==
                        userId &&
                    alert.BillStreamId ==
                        null &&
                    alert.BillChangeId ==
                        null &&
                    alert.AlertType ==
                        BillAlertType.PaydayPlan &&
                    alert.SourceEventId ==
                        sourceEventId,
                cancellationToken);
    }

    private static PlanningPaydayAlertRequest Normalize(
        PlanningPaydayAlertRequest request)
    {
        var title =
            request.Title?.Trim()
            ?? string.Empty;

        var message =
            request.Message?.Trim()
            ?? string.Empty;

        if (title.Length ==
            0)
        {
            throw new ArgumentException(
                "Alert title is required.",
                nameof(request));
        }

        if (title.Length >
            MaxTitleLength)
        {
            throw new ArgumentException(
                $"Alert title must not exceed {MaxTitleLength} characters.",
                nameof(request));
        }

        if (message.Length ==
            0)
        {
            throw new ArgumentException(
                "Alert message is required.",
                nameof(request));
        }

        if (message.Length >
            MaxMessageLength)
        {
            throw new ArgumentException(
                $"Alert message must not exceed {MaxMessageLength} characters.",
                nameof(request));
        }

        if (!Enum.IsDefined(
                request.Severity))
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                "Alert severity is unsupported.");
        }

        return request with
        {
            Title =
                title,
            Message =
                message
        };
    }

    private static void EnsureIdentical(
        BillAlertEntity existing,
        PlanningPaydayAlertRequest requested)
    {
        if (existing.Severity !=
                ToEntitySeverity(
                    requested.Severity) ||
            !string.Equals(
                existing.Title,
                requested.Title,
                StringComparison.Ordinal) ||
            !string.Equals(
                existing.Message,
                requested.Message,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A different payday-plan alert is already recorded for this source event.");
        }
    }

    private static BillAlertSeverity ToEntitySeverity(
        PlanningPaydayAlertSeverity severity) =>
        severity switch
        {
            PlanningPaydayAlertSeverity.Info =>
                BillAlertSeverity.Info,
            PlanningPaydayAlertSeverity.Warning =>
                BillAlertSeverity.Warning,
            PlanningPaydayAlertSeverity.Critical =>
                BillAlertSeverity.Critical,
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(severity))
        };
}

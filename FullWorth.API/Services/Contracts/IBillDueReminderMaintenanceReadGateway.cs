namespace FullWorth.API.Services.Contracts;

/// <summary>
/// Bills-owned owner-scoped read boundary for date-driven statement reminders.
/// No statement text, account identifiers, or storage paths cross this boundary.
/// </summary>
public sealed record BillDueReminderMaintenanceCandidate(Guid UserId, Guid BillStreamId);

public interface IBillDueReminderMaintenanceReadGateway
{
    Task<IReadOnlyList<BillDueReminderMaintenanceCandidate>> GetCandidatesAsync(
        CancellationToken cancellationToken = default);

    // null means no stream belongs to this owner; false means owned and inactive.
    Task<bool?> IsOwnedActiveAsync(
        Guid userId, Guid billStreamId, CancellationToken cancellationToken = default);
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

/// <summary>Bills owns all direct reads of BillStreams and BillAlerts.</summary>
public sealed class BillDueReminderMaintenanceReadGateway(FullWorthDbContext db)
    : IBillDueReminderMaintenanceReadGateway
{
    public async Task<IReadOnlyList<BillDueReminderMaintenanceCandidate>> GetCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        // Enforce matching owner on both sides even if the database ever
        // contains a malformed alert pointing to a foreign stream.
        var pairs = await (
            from alert in db.BillAlerts.AsNoTracking()
            join stream in db.BillStreams.AsNoTracking()
                on new { AlertStreamId = alert.BillStreamId, alert.UserId }
                equals new { AlertStreamId = (Guid?)stream.Id, stream.UserId }
            where alert.AlertType == BillAlertType.PaymentDue
            select new { alert.UserId, BillStreamId = stream.Id }
        ).Distinct().ToListAsync(cancellationToken);

        return pairs.Select(item =>
            new BillDueReminderMaintenanceCandidate(item.UserId, item.BillStreamId)).ToArray();
    }

    public async Task<bool?> IsOwnedActiveAsync(
        Guid userId, Guid billStreamId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || billStreamId == Guid.Empty)
            throw new ArgumentException("A valid owner and bill identity are required.");

        return await db.BillStreams.AsNoTracking()
            .Where(stream => stream.UserId == userId && stream.Id == billStreamId)
            .Select(stream => (bool?)stream.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
    }
}

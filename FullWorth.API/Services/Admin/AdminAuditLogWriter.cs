using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Admin;

public sealed class AdminAuditLogWriter(
    FullWorthDbContext dbContext)
    : IAdminAuditLogWriter
{
    public void Stage(
        AdminAuditLogWrite audit)
    {
        ArgumentNullException.ThrowIfNull(
            audit);

        if (audit.ActorUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "Actor user ID is required.",
                nameof(audit));
        }

        if (string.IsNullOrWhiteSpace(audit.Action))
        {
            throw new ArgumentException(
                "Audit action is required.",
                nameof(audit));
        }

        if (string.IsNullOrWhiteSpace(audit.SubjectType))
        {
            throw new ArgumentException(
                "Audit subject type is required.",
                nameof(audit));
        }

        dbContext.AdminAuditLogs.Add(
            new AdminAuditLogEntity
            {
                ActorUserId = audit.ActorUserId,
                TargetUserId = audit.TargetUserId,
                Action = audit.Action,
                SubjectType = audit.SubjectType,
                SubjectId = audit.SubjectId,
                CreatedAtUtc = audit.CreatedAtUtc
            });

        /*
         * Do not save here. Callers stage owner-specific changes into the
         * shared scoped modular-monolith unit of work and commit once.
         */
    }
}

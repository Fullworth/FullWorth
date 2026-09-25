namespace FullWorth.API.Services.Contracts;

public sealed record AdminAuditLogWrite(
    Guid ActorUserId,
    Guid? TargetUserId,
    string Action,
    string SubjectType,
    Guid? SubjectId,
    DateTimeOffset CreatedAtUtc);

public interface IAdminAuditLogWriter
{
    void Stage(AdminAuditLogWrite audit);
}

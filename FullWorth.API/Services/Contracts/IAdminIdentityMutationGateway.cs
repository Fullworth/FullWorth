namespace FullWorth.API.Services.Contracts;

public sealed record AdminIdentityManagementSnapshot(
    IReadOnlyList<string> ActorRoles,
    IReadOnlyList<string> TargetRoles);

public sealed record AdminIdentityRoleMutationResult(
    bool Found,
    bool Changed,
    Guid? RoleId);

public interface IAdminIdentityMutationGateway
{
    Task<AdminIdentityManagementSnapshot?> GetManagementSnapshotAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken = default);

    Task<AdminIdentityRoleMutationResult> StageAssignRoleAsync(
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken = default);

    Task<AdminIdentityRoleMutationResult> StageRemoveRoleAsync(
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken = default);
}

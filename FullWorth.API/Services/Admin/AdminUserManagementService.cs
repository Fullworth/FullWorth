using System.Data;
using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FullWorth.API.Services.Admin;

public sealed class AdminUserManagementService(
    FullWorthDbContext dbContext,
    IAdminIdentityMutationGateway identityGateway,
    IAdminSubscriptionMutationGateway subscriptionGateway,
    IAdminAuditLogWriter auditLogWriter,
    TimeProvider timeProvider)
{
    public async Task<AdminUserMutationResult> AssignRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId ||
            !FullWorthRoles.IsStaffRole(roleName) ||
            roleName == FullWorthRoles.Owner)
        {
            return AdminUserMutationResult.Forbidden;
        }

        return await InTransactionAsync(
            async () =>
            {
                var state =
                    await LoadManagementStateAsync(
                        actorUserId,
                        targetUserId,
                        cancellationToken);

                if (state is null ||
                    !FullWorthRoleHierarchy.CanManageUser(
                        state.ActorHighestRole!,
                        state.TargetHighestRole) ||
                    !FullWorthRoleHierarchy.CanAssignRole(
                        state.ActorHighestRole!,
                        roleName))
                {
                    return AdminUserMutationResult.Forbidden;
                }

                var mutation =
                    await identityGateway.StageAssignRoleAsync(
                        targetUserId,
                        roleName,
                        cancellationToken);

                if (!mutation.Found ||
                    !mutation.RoleId.HasValue)
                {
                    return AdminUserMutationResult.NotFound;
                }

                if (mutation.Changed)
                {
                    AddAudit(
                        actorUserId,
                        targetUserId,
                        "StaffRoleAssigned",
                        mutation.RoleId.Value);

                    await dbContext.SaveChangesAsync(
                        cancellationToken);
                }

                return AdminUserMutationResult.Success;
            },
            cancellationToken);
    }

    public async Task<AdminUserMutationResult> RemoveRoleAsync(
        Guid actorUserId,
        Guid targetUserId,
        string roleName,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId == targetUserId ||
            !FullWorthRoles.IsStaffRole(roleName) ||
            roleName == FullWorthRoles.Owner)
        {
            return AdminUserMutationResult.Forbidden;
        }

        return await InTransactionAsync(
            async () =>
            {
                var state =
                    await LoadManagementStateAsync(
                        actorUserId,
                        targetUserId,
                        cancellationToken);

                if (state is null ||
                    !FullWorthRoleHierarchy.CanManageUser(
                        state.ActorHighestRole!,
                        state.TargetHighestRole))
                {
                    return AdminUserMutationResult.Forbidden;
                }

                var mutation =
                    await identityGateway.StageRemoveRoleAsync(
                        targetUserId,
                        roleName,
                        cancellationToken);

                if (!mutation.Found ||
                    !mutation.RoleId.HasValue)
                {
                    return AdminUserMutationResult.NotFound;
                }

                if (mutation.Changed)
                {
                    AddAudit(
                        actorUserId,
                        targetUserId,
                        "StaffRoleRemoved",
                        mutation.RoleId.Value);

                    await dbContext.SaveChangesAsync(
                        cancellationToken);
                }

                return AdminUserMutationResult.Success;
            },
            cancellationToken);
    }

    public async Task<AdminUserMutationResult> GrantEntitlementAsync(
        Guid actorUserId,
        Guid targetUserId,
        FullWorthSubscriptionTier tier,
        int? durationDays,
        bool grantsLifetimeAccess,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(tier) ||
            grantsLifetimeAccess == durationDays.HasValue ||
            durationDays is <= 0 or > 3650)
        {
            return AdminUserMutationResult.Invalid;
        }

        var state =
            await LoadManagementStateAsync(
                actorUserId,
                targetUserId,
                cancellationToken);

        if (state is null ||
            !FullWorthRoleHierarchy.CanManageUser(
                state.ActorHighestRole!,
                state.TargetHighestRole))
        {
            return AdminUserMutationResult.Forbidden;
        }

        var nowUtc =
            timeProvider.GetUtcNow();

        var entitlementId =
            subscriptionGateway.StageGrantEntitlement(
                targetUserId,
                tier.ToString(),
                durationDays,
                grantsLifetimeAccess,
                actorUserId,
                nowUtc);

        AddAudit(
            actorUserId,
            targetUserId,
            "SubscriptionEntitlementGranted",
            entitlementId);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return AdminUserMutationResult.SuccessWithId(
            entitlementId);
    }

    public async Task<AdminUserMutationResult> RevokeEntitlementAsync(
        Guid actorUserId,
        Guid targetUserId,
        Guid entitlementId,
        CancellationToken cancellationToken = default)
    {
        var state =
            await LoadManagementStateAsync(
                actorUserId,
                targetUserId,
                cancellationToken);

        if (state is null ||
            !FullWorthRoleHierarchy.CanManageUser(
                state.ActorHighestRole!,
                state.TargetHighestRole))
        {
            return AdminUserMutationResult.Forbidden;
        }

        var nowUtc =
            timeProvider.GetUtcNow();

        var mutation =
            await subscriptionGateway.StageRevokeEntitlementAsync(
                targetUserId,
                entitlementId,
                nowUtc,
                cancellationToken);

        if (!mutation.Found)
        {
            return AdminUserMutationResult.NotFound;
        }

        if (mutation.Changed)
        {
            AddAudit(
                actorUserId,
                targetUserId,
                "SubscriptionEntitlementRevoked",
                entitlementId);

            await dbContext.SaveChangesAsync(
                cancellationToken);
        }

        return AdminUserMutationResult.Success;
    }

    public async Task<AdminUserMutationResult> SetProgramMembershipAsync(
        Guid actorUserId,
        Guid targetUserId,
        UserProgramType program,
        bool isActive,
        DateTimeOffset? endsAtUtc,
        CancellationToken cancellationToken = default)
    {
        var nowUtc =
            timeProvider.GetUtcNow();

        if (!Enum.IsDefined(program) ||
            endsAtUtc <= nowUtc)
        {
            return AdminUserMutationResult.Invalid;
        }

        var state =
            await LoadManagementStateAsync(
                actorUserId,
                targetUserId,
                cancellationToken);

        if (state is null ||
            !FullWorthRoleHierarchy.CanManageUser(
                state.ActorHighestRole!,
                state.TargetHighestRole))
        {
            return AdminUserMutationResult.Forbidden;
        }

        var membershipId =
            await subscriptionGateway.StageSetProgramMembershipAsync(
                targetUserId,
                program.ToString(),
                isActive,
                endsAtUtc,
                actorUserId,
                nowUtc,
                cancellationToken);

        AddAudit(
            actorUserId,
            targetUserId,
            isActive
                ? "UserProgramMembershipEnabled"
                : "UserProgramMembershipDisabled",
            membershipId);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return AdminUserMutationResult.SuccessWithId(
            membershipId);
    }

    private async Task<ManagementState?> LoadManagementStateAsync(
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var snapshot =
            await identityGateway.GetManagementSnapshotAsync(
                actorUserId,
                targetUserId,
                cancellationToken);

        if (snapshot is null)
        {
            return null;
        }

        var actorHighest =
            snapshot.ActorRoles
                .OrderByDescending(
                    FullWorthRoleHierarchy.GetRank)
                .FirstOrDefault();

        if (!FullWorthRoles.IsStaffRole(
                actorHighest))
        {
            return null;
        }

        var targetHighest =
            snapshot.TargetRoles
                .OrderByDescending(
                    FullWorthRoleHierarchy.GetRank)
                .FirstOrDefault();

        return new ManagementState(
            actorHighest,
            targetHighest);
    }

    private void AddAudit(
        Guid actorUserId,
        Guid targetUserId,
        string action,
        Guid subjectId)
    {
        auditLogWriter.Stage(
            new AdminAuditLogWrite(
                actorUserId,
                targetUserId,
                action,
                "UserAdministration",
                subjectId,
                timeProvider.GetUtcNow()));
    }

    private async Task<AdminUserMutationResult> InTransactionAsync(
        Func<Task<AdminUserMutationResult>> action,
        CancellationToken cancellationToken)
    {
        IDbContextTransaction? transaction =
            null;

        try
        {
            if (dbContext.Database.IsRelational())
            {
                transaction =
                    await dbContext.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        cancellationToken);
            }

            var result =
                await action();

            if (transaction is not null &&
                result.Succeeded)
            {
                await transaction.CommitAsync(
                    cancellationToken);
            }

            return result;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }

    private sealed record ManagementState(
        string? ActorHighestRole,
        string? TargetHighestRole);
}

public sealed record AdminUserMutationResult(
    bool Succeeded,
    string Code,
    Guid? ResourceId = null)
{
    public static AdminUserMutationResult Success { get; } =
        new(true, "success");

    public static AdminUserMutationResult Forbidden { get; } =
        new(false, "forbidden");

    public static AdminUserMutationResult NotFound { get; } =
        new(false, "not_found");

    public static AdminUserMutationResult Invalid { get; } =
        new(false, "invalid");

    public static AdminUserMutationResult SuccessWithId(
        Guid id) =>
        new(true, "success", id);
}

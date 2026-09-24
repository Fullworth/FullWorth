using System.Security.Cryptography;
using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Identity;

public sealed class AdminIdentityMutationGateway(
    FullWorthDbContext dbContext)
    : IAdminIdentityMutationGateway
{
    public async Task<AdminIdentityManagementSnapshot?>
        GetManagementSnapshotAsync(
            Guid actorUserId,
            Guid targetUserId,
            CancellationToken cancellationToken = default)
    {
        if (actorUserId == Guid.Empty ||
            targetUserId == Guid.Empty)
        {
            return null;
        }

        var targetUser =
            await dbContext.Users
                .SingleOrDefaultAsync(
                    user =>
                        user.Id == targetUserId,
                    cancellationToken);

        if (targetUser is null)
        {
            return null;
        }

        var assignments =
            await (
                from userRole in dbContext.UserRoles
                join role in dbContext.Roles
                    on userRole.RoleId equals role.Id
                where userRole.UserId == actorUserId ||
                      userRole.UserId == targetUserId
                select new
                {
                    userRole.UserId,
                    role.Name
                })
                .ToListAsync(cancellationToken);

        var actorRoles =
            assignments
                .Where(item => item.UserId == actorUserId)
                .Select(item => item.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        var targetRoles =
            assignments
                .Where(item => item.UserId == targetUserId)
                .Select(item => item.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        return new AdminIdentityManagementSnapshot(
            actorRoles,
            targetRoles);
    }

    public async Task<AdminIdentityRoleMutationResult>
        StageAssignRoleAsync(
            Guid targetUserId,
            string roleName,
            CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: null);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        var targetExists =
            await dbContext.Users
                .AsNoTracking()
                .AnyAsync(
                    user =>
                        user.Id == targetUserId,
                    cancellationToken);

        if (!targetExists)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: null);
        }

        var normalizedRoleName =
            roleName.Trim().ToUpperInvariant();

        var role =
            await dbContext.Roles
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.NormalizedName ==
                            normalizedRoleName,
                    cancellationToken);

        if (role is null)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: null);
        }

        var exists =
            await dbContext.UserRoles
                .AnyAsync(
                    userRole =>
                        userRole.UserId == targetUserId &&
                        userRole.RoleId == role.Id,
                    cancellationToken);

        if (exists)
        {
            return new AdminIdentityRoleMutationResult(
                Found: true,
                Changed: false,
                RoleId: role.Id);
        }

        StageSessionRevocation(
            targetUser);

        dbContext.UserRoles.Add(
            new IdentityUserRole<Guid>
            {
                UserId = targetUserId,
                RoleId = role.Id
            });

        return new AdminIdentityRoleMutationResult(
            Found: true,
            Changed: true,
            RoleId: role.Id);
    }

    public async Task<AdminIdentityRoleMutationResult>
        StageRemoveRoleAsync(
            Guid targetUserId,
            string roleName,
            CancellationToken cancellationToken = default)
    {
        if (targetUserId == Guid.Empty)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: null);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        var normalizedRoleName =
            roleName.Trim().ToUpperInvariant();

        var role =
            await dbContext.Roles
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.NormalizedName ==
                            normalizedRoleName,
                    cancellationToken);

        if (role is null)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: null);
        }

        var userRole =
            await dbContext.UserRoles
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.UserId == targetUserId &&
                        candidate.RoleId == role.Id,
                    cancellationToken);

        if (userRole is null)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: role.Id);
        }

        var targetUser =
            await dbContext.Users
                .SingleOrDefaultAsync(
                    user =>
                        user.Id == targetUserId,
                    cancellationToken);

        if (targetUser is null)
        {
            return new AdminIdentityRoleMutationResult(
                Found: false,
                Changed: false,
                RoleId: role.Id);
        }

        StageSessionRevocation(
            targetUser);

        dbContext.UserRoles.Remove(
            userRole);

        return new AdminIdentityRoleMutationResult(
            Found: true,
            Changed: true,
            RoleId: role.Id);
    }

    private static void StageSessionRevocation(
        FullWorth.API.Data.Entities.ApplicationUser user)
    {
        /*
         * SecurityStamp is an opaque revocation secret. Role mutations are
         * staged directly in this Identity-owned gateway so stamp rotation is
         * staged here too and committed atomically with the role row and
         * Admin audit record by the shared scoped DbContext unit of work.
         */
        user.SecurityStamp =
            Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32))
                .ToLowerInvariant();
    }
}

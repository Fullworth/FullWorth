using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Admin;
using FullWorth.API.Services.Identity;
using FullWorth.API.Services.Subscriptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Security;

public sealed class AdminUserManagementServiceTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AssignRoleAsync_OwnerCanAssignAdminAndAudit()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, FullWorthRoles.Owner);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).AssignRoleAsync(
            owner.Id,
            target.Id,
            FullWorthRoles.Admin);

        Assert.True(result.Succeeded);
        Assert.True(await HasRole(dbContext, target.Id, FullWorthRoles.Admin));
        Assert.Single(dbContext.AdminAuditLogs);
    }

    [Fact]
    public async Task AssignRoleAsync_NeverAssignsOwnerThroughNormalPath()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, FullWorthRoles.Owner);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).AssignRoleAsync(
            owner.Id,
            target.Id,
            FullWorthRoles.Owner);

        Assert.False(result.Succeeded);
        Assert.False(await HasRole(dbContext, target.Id, FullWorthRoles.Owner));
    }

    [Fact]
    public async Task RemoveRoleAsync_RejectsSelfDemotion()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        await AssignSeededRole(dbContext, owner.Id, FullWorthRoles.Owner);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).RemoveRoleAsync(
            owner.Id,
            owner.Id,
            FullWorthRoles.Owner);

        Assert.False(result.Succeeded);
        Assert.True(await HasRole(dbContext, owner.Id, FullWorthRoles.Owner));
    }

    [Fact]
    public async Task AssignRoleAsync_AdminCannotManagePeer()
    {
        await using var dbContext = CreateDbContext();
        var actor = AddUser(dbContext, "actor@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, actor.Id, FullWorthRoles.Admin);
        await AssignSeededRole(dbContext, target.Id, FullWorthRoles.Admin);
        await dbContext.SaveChangesAsync();

        var result = await CreateService(dbContext).AssignRoleAsync(
            actor.Id,
            target.Id,
            FullWorthRoles.Moderator);

        Assert.False(result.Succeeded);
        Assert.False(await HasRole(dbContext, target.Id, FullWorthRoles.Moderator));
        Assert.Empty(dbContext.AdminAuditLogs);
    }

    [Fact]
    public async Task GrantAndRevokeEntitlement_AreTargetScopedAndAudited()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, FullWorthRoles.Owner);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        var grant = await service.GrantEntitlementAsync(
            owner.Id,
            target.Id,
            FullWorthSubscriptionTier.Standard,
            durationDays: 30,
            grantsLifetimeAccess: false);

        Assert.True(grant.Succeeded);
        Assert.NotNull(grant.ResourceId);
        Assert.True((await service.RevokeEntitlementAsync(
            owner.Id,
            target.Id,
            grant.ResourceId!.Value)).Succeeded);

        var entitlement = await dbContext.SubscriptionEntitlements.SingleAsync();
        Assert.True(entitlement.IsRevoked);
        Assert.Equal(NowUtc, entitlement.RevokedAtUtc);
        Assert.Equal(2, await dbContext.AdminAuditLogs.CountAsync());
    }

    [Fact]
    public async Task SetProgramMembershipAsync_UpdatesMembershipWithoutGrantingStaffRole()
    {
        await using var dbContext = CreateDbContext();
        var owner = AddUser(dbContext, "owner@example.com");
        var target = AddUser(dbContext, "target@example.com");
        await AssignSeededRole(dbContext, owner.Id, FullWorthRoles.Owner);
        await dbContext.SaveChangesAsync();
        var service = CreateService(dbContext);

        Assert.True((await service.SetProgramMembershipAsync(
            owner.Id,
            target.Id,
            UserProgramType.BetaTester,
            isActive: true,
            endsAtUtc: NowUtc.AddDays(90))).Succeeded);

        var membership = await dbContext.UserProgramMemberships.SingleAsync();
        Assert.True(membership.IsActive);
        Assert.Equal(UserProgramType.BetaTester, membership.Program);
        Assert.False(await HasRole(dbContext, target.Id, FullWorthRoles.Moderator));
        Assert.Empty(dbContext.SubscriptionEntitlements);
    }

    private static ApplicationUser AddUser(
        FullWorthDbContext dbContext,
        string email)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant()
        };
        dbContext.Users.Add(user);
        return user;
    }

    private static async Task AssignSeededRole(
        FullWorthDbContext dbContext,
        Guid userId,
        string roleName)
    {
        var role = await dbContext.Roles.SingleAsync(
            candidate => candidate.Name == roleName);
        dbContext.UserRoles.Add(
            new IdentityUserRole<Guid>
            {
                UserId = userId,
                RoleId = role.Id
            });
    }

    private static async Task<bool> HasRole(
        FullWorthDbContext dbContext,
        Guid userId,
        string roleName)
    {
        return await (
            from userRole in dbContext.UserRoles
            join role in dbContext.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId && role.Name == roleName
            select userRole).AnyAsync();
    }

    private static AdminUserManagementService CreateService(
        FullWorthDbContext dbContext)
    {
        return new AdminUserManagementService(
            dbContext,
            new AdminIdentityMutationGateway(dbContext),
            new AdminSubscriptionMutationGateway(dbContext),
            new AdminAuditLogWriter(dbContext),
            new FixedTimeProvider(NowUtc));
    }

    private static FullWorthDbContext CreateDbContext()
    {
        var dbContext = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options);
        dbContext.Database.EnsureCreated();
        return dbContext;
    }

    private sealed class FixedTimeProvider(DateTimeOffset value)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }
}

using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Admin;
using FullWorth.API.Services.Identity;
using FullWorth.API.Services.Subscriptions;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FullWorth.Tests.Security;

public sealed class AdminGrantOperationRecoveryTests
{
    private static readonly DateTimeOffset NowUtc =
        new(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LostResponseRetryReturnsTheOriginalGrantAndAuditsOnce()
    {
        await using var db = CreateDbContext();
        var owner = AddUser(db, "owner@example.test");
        var target = AddUser(db, "target@example.test");
        await AssignSeededRole(db, owner.Id, FullWorthRoles.Owner);
        await db.SaveChangesAsync();
        var operation = Guid.NewGuid();
        var service = CreateService(db);
        var first = await service.GrantEntitlementAsync(owner.Id, target.Id, FullWorthSubscriptionTier.Standard,
            30, false, operationId: operation);
        db.ChangeTracker.Clear(); // A fresh request must reconcile persisted facts, not a retained DTO.
        var retry = await CreateService(db).GrantEntitlementAsync(owner.Id, target.Id, FullWorthSubscriptionTier.Standard,
            30, false, operationId: operation);
        Assert.True(first.Succeeded);
        Assert.Equal(first.ResourceId, retry.ResourceId);
        Assert.Equal(operation, retry.ResourceId);
        Assert.Single(await db.SubscriptionEntitlements.ToListAsync());
        Assert.Single(await db.AdminAuditLogs.ToListAsync());

        var changed = await service.GrantEntitlementAsync(owner.Id, target.Id, FullWorthSubscriptionTier.Standard,
            31, false, operationId: operation);
        Assert.False(changed.Succeeded);
        Assert.Single(await db.SubscriptionEntitlements.ToListAsync());
    }

    [Fact]
    public async Task NewIntentCreatesSeparateGrantButReplayStillRequiresCurrentHierarchy()
    {
        await using var db = CreateDbContext();
        var owner = AddUser(db, "owner@example.test");
        var target = AddUser(db, "target@example.test");
        await AssignSeededRole(db, owner.Id, FullWorthRoles.Owner);
        await db.SaveChangesAsync();
        var operation = Guid.NewGuid();
        var service = CreateService(db);
        Assert.True((await service.GrantEntitlementAsync(owner.Id, target.Id, FullWorthSubscriptionTier.Standard,
            30, false, operationId: operation)).Succeeded);
        Assert.True((await service.GrantEntitlementAsync(owner.Id, target.Id, FullWorthSubscriptionTier.Standard,
            30, false, operationId: Guid.NewGuid())).Succeeded);
        Assert.Equal(2, await db.SubscriptionEntitlements.CountAsync());
        Assert.False((await service.GrantEntitlementAsync(target.Id, owner.Id, FullWorthSubscriptionTier.Standard,
            30, false, operationId: operation)).Succeeded);
        Assert.False((await service.GrantEntitlementAsync(owner.Id, target.Id, FullWorthSubscriptionTier.Standard,
            30, false, operationId: Guid.Empty)).Succeeded);
        Assert.Equal(2, await db.AdminAuditLogs.CountAsync());
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
            new FixedTimeProvider(NowUtc),
            NullLoggerFactory.Instance,
            TestSecurityAlertAggregator.Create());
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

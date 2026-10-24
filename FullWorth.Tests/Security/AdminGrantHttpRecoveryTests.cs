using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class AdminGrantHttpRecoveryTests
{
    private const string TestPassword =
        "FullWorth!Tests123";


    [Fact]
    public async Task RetriedOperationKeepsOneGrantAndStillRequiresCredentials()
    {
        await using var factory = new FullWorthApiFactory();
        using var staff = factory.CreateHttpsClient();
        using var targetClient = factory.CreateHttpsClient();
        var actor = await TestUserAuthentication.RegisterWithRoleAndLoginAsync(factory, staff, FullWorthRoles.Owner);
        var target = await TestUserAuthentication.RegisterAndLoginAsync(targetClient);
        var targetId = await TestUserAuthentication.GetUserIdAsync(factory, target.Email);
        TestUserAuthentication.Authorize(staff, actor);
        var operation = Guid.NewGuid();
        var path = $"/api/admin/users/{targetId:D}/entitlements";
        var payload = new { tier = "Standard", durationDays = 30, grantsLifetimeAccess = false,
            currentPassword = TestPassword, operationId = operation };
        using var first = await staff.PostAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var retry = await staff.PostAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var a = await first.Content.ReadFromJsonAsync<GrantResponse>();
        var b = await retry.Content.ReadFromJsonAsync<GrantResponse>();
        Assert.Equal(operation, a!.EntitlementId);
        Assert.Equal(a.EntitlementId, b!.EntitlementId);
        using var missingProof = await staff.PostAsJsonAsync(path,
            new { tier = "Standard", durationDays = 30, grantsLifetimeAccess = false, currentPassword = "", operationId = operation });
        Assert.Equal(HttpStatusCode.Unauthorized, missingProof.StatusCode);
        using var changed = await staff.PostAsJsonAsync(path,
            new { tier = "Standard", durationDays = 31, grantsLifetimeAccess = false,
                currentPassword = TestPassword, operationId = operation });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        Assert.Single(await db.SubscriptionEntitlements.Where(item => item.UserId == targetId).ToListAsync());
        Assert.Single(await db.AdminAuditLogs.Where(item => item.TargetUserId == targetId &&
            item.Action == "SubscriptionEntitlementGranted").ToListAsync());
    }

    private sealed record GrantResponse(Guid EntitlementId);
}

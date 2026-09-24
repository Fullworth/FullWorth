using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class AccountPasswordAuthorizationTests
{
    [Fact]
    public async Task ChangePassword_AnonymousUser_IsRejected()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/account/security/password",
                new
                {
                    currentPassword = "x",
                    newPassword = "y",
                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task IdentityPasswordChange_RotatesSecurityStamp()
    {
        await using var factory =
            new FullWorthApiFactory();

        await using var scope =
            factory.Services
                .CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var email =
            $"password-stamp-{Guid.NewGuid():N}@fullworth.local";

        var user =
            new ApplicationUser
            {
                Id =
                    Guid.NewGuid(),

                UserName =
                    email,

                Email =
                    email
            };

        var createResult =
            await userManager.CreateAsync(
                user,
                "FullWorth!Initial123");

        Assert.True(
            createResult.Succeeded);

        var originalStamp =
            await userManager.GetSecurityStampAsync(
                user);

        var changeResult =
            await userManager.ChangePasswordAsync(
                user,
                "FullWorth!Initial123",
                "FullWorth!Replacement456");

        Assert.True(
            changeResult.Succeeded);

        var changedStamp =
            await userManager.GetSecurityStampAsync(
                user);

        Assert.False(
            string.IsNullOrWhiteSpace(
                originalStamp));

        Assert.False(
            string.IsNullOrWhiteSpace(
                changedStamp));

        Assert.NotEqual(
            originalStamp,
            changedStamp);
    }

    [Fact]
    public async Task BearerTokenLifetimes_AreExplicitlyHardened()
    {
        await using var factory =
            new FullWorthApiFactory();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        BearerTokenOptions>>()
                .Get(
                    IdentityConstants
                        .BearerScheme);

        Assert.Equal(
            TimeSpan.FromMinutes(15),
            options.BearerTokenExpiration);

        Assert.Equal(
            TimeSpan.FromDays(14),
            options.RefreshTokenExpiration);
    }
}

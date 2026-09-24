using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class AccountSessionRevocationTests
{
    private const string TestPassword =
        "FullWorth!Tests123";

    [Fact]
    public async Task RevokeAllSessions_AnonymousUser_IsRejected()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/account/security/sessions/revoke-all",
                new
                {
                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_ValidReauthentication_InvalidatesEveryExistingRefreshToken()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var firstSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        var secondSession =
            await TestUserAuthentication
                .LoginAsync(
                    client,
                    firstSession.Email);

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                firstSession.Email);

        TestUserAuthentication.Authorize(
            client,
            firstSession);

        using var revokeResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/sessions/revoke-all",
                new
                {
                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.NoContent,
            revokeResponse.StatusCode);

        var updatedSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                firstSession.Email);

        Assert.NotEqual(
            originalSecurityStamp,
            updatedSecurityStamp);

        client.DefaultRequestHeaders.Authorization =
            null;

        using var firstRefreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        firstSession.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            firstRefreshResponse.StatusCode);

        using var secondRefreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        secondSession.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            secondRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task RevokeAllSessions_InvalidPassword_DoesNotRevokeRefreshSession()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                session.Email);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var revokeResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/sessions/revoke-all",
                new
                {
                    currentPassword =
                        "FullWorth!Wrong123",

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            revokeResponse.StatusCode);

        var unchangedSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                session.Email);

        Assert.Equal(
            originalSecurityStamp,
            unchangedSecurityStamp);

        client.DefaultRequestHeaders.Authorization =
            null;

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        session.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.OK,
            refreshResponse.StatusCode);
    }

    private static async Task<string>
        GetSecurityStampAsync(
            FullWorthApiFactory factory,
            string email)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(
            user);

        return await userManager
            .GetSecurityStampAsync(
                user!);
    }
}

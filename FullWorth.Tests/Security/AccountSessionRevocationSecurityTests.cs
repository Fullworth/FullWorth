using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class AccountSessionRevocationSecurityTests
{
    private const string Password =
        "FullWorth!Tests123";

    [Fact]
    public async Task RevokeAllSessions_RevokesEveryRefreshFamilyAndInitialToken()
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

        var initialOnlySession =
            await TestUserAuthentication
                .LoginAsync(
                    client,
                    firstSession.Email);

        var firstFamily =
            await EnrollFamilyAsync(
                client,
                firstSession.RefreshToken);

        var secondFamily =
            await EnrollFamilyAsync(
                client,
                secondSession.RefreshToken);

        TestUserAuthentication.Authorize(
            client,
            firstFamily);

        using var revokeResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/sessions/revoke-all",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.NoContent,
            revokeResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization =
            null;

        await AssertRefreshRejectedAsync(
            client,
            firstFamily.RefreshToken);

        await AssertRefreshRejectedAsync(
            client,
            secondFamily.RefreshToken);

        await AssertRefreshRejectedAsync(
            client,
            initialOnlySession.RefreshToken);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    FullWorthDbContext>();

        var userId =
            await dbContext.Users
                .Where(
                    user =>
                        user.Email ==
                            firstSession.Email)
                .Select(
                    user =>
                        user.Id)
                .SingleAsync();

        var remainingFamilies =
            await dbContext.UserTokens
                .CountAsync(
                    token =>
                        token.UserId == userId &&
                        token.LoginProvider ==
                            "FullWorth.RefreshFamily");

        Assert.Equal(
            0,
            remainingFamilies);
    }

    [Fact]
    public async Task RevokeAllSessions_WrongPassword_PreservesExistingFamily()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        var family =
            await EnrollFamilyAsync(
                client,
                session.RefreshToken);

        TestUserAuthentication.Authorize(
            client,
            family);

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

        client.DefaultRequestHeaders.Authorization =
            null;

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        family.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.OK,
            refreshResponse.StatusCode);
    }

    private static async Task<TestUserSession>
        EnrollFamilyAsync(
            HttpClient client,
            string refreshToken)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken
                });

        response.EnsureSuccessStatusCode();

        var rotated =
            await response.Content
                .ReadFromJsonAsync<
                    TokenResponse>();

        Assert.NotNull(
            rotated);

        return new TestUserSession(
            Email:
                "family@fullworth.local",
            AccessToken:
                rotated!.AccessToken,
            RefreshToken:
                rotated.RefreshToken);
    }

    private static async Task AssertRefreshRejectedAsync(
        HttpClient client,
        string refreshToken)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    private sealed record TokenResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}

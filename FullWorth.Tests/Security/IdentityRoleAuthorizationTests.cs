using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Legal;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class IdentityRoleAuthorizationTests
{
    private const string Password =
        "FullWorth!RoleTests123";

    [Fact]
    public async Task OwnerRoleAssignedBeforeLogin_AuthorizesAdminEndpoint()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"owner-role-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        await AssignOwnerRoleAsync(
            factory,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                loginResult.AccessToken);

        using var adminResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.OK,
            adminResponse.StatusCode);
    }

    [Fact]
    public async Task OwnerRefreshToken_IssuesRoleAwareAccessToken()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"owner-refresh-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        await AssignOwnerRoleAsync(
            factory,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        loginResult.RefreshToken
                });

        refreshResponse.EnsureSuccessStatusCode();

        var refreshResult =
            await refreshResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            refreshResult);

        Assert.False(
            string.IsNullOrWhiteSpace(
                refreshResult!.AccessToken));

        Assert.False(
            string.IsNullOrWhiteSpace(
                refreshResult.RefreshToken));

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                refreshResult.AccessToken);

        using var adminResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.OK,
            adminResponse.StatusCode);
    }

    [Fact]
    public async Task RefreshToken_IsSingleUseAndRotatedTokenCanBeUsedOnce()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"refresh-replay-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        using var firstRefreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        loginResult.RefreshToken
                });

        firstRefreshResponse.EnsureSuccessStatusCode();

        var firstRefresh =
            await firstRefreshResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            firstRefresh);

        using var replayOriginalResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        loginResult.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayOriginalResponse.StatusCode);

        using var secondRefreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        firstRefresh!.RefreshToken
                });

        secondRefreshResponse.EnsureSuccessStatusCode();

        var secondRefresh =
            await secondRefreshResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            secondRefresh);

        using var replayRotatedResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        firstRefresh.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayRotatedResponse.StatusCode);

        Assert.NotEqual(
            loginResult.RefreshToken,
            firstRefresh.RefreshToken);

        Assert.NotEqual(
            firstRefresh.RefreshToken,
            secondRefresh!.RefreshToken);

        await using var verificationScope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    FullWorthDbContext>();

        var refreshFamilyRows =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.LoginProvider ==
                            "FullWorth.RefreshFamily")
                .ToListAsync();

        var refreshFamily =
            Assert.Single(
                refreshFamilyRows);

        Assert.DoesNotContain(
            loginResult.RefreshToken,
            refreshFamily.Value ?? string.Empty,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            firstRefresh.RefreshToken,
            refreshFamily.Value ?? string.Empty,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secondRefresh.RefreshToken,
            refreshFamily.Value ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshFamilyLimit_DoesNotEvictActiveReplayProtection()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"refresh-family-limit-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        var bearerOptions =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        BearerTokenOptions>>()
                .Get(
                    IdentityConstants
                        .BearerScheme);

        var loginRefreshTicket =
            bearerOptions
                .RefreshTokenProtector
                .Unprotect(
                    loginResult.RefreshToken);

        Assert.NotNull(
            loginRefreshTicket);

        var firstGenerationTokens =
            Enumerable
                .Range(
                    0,
                    17)
                .Select(
                    _ =>
                        bearerOptions
                            .RefreshTokenProtector
                            .Protect(
                                loginRefreshTicket!))
                .ToArray();

        Assert.Equal(
            17,
            firstGenerationTokens
                .Distinct(
                    StringComparer.Ordinal)
                .Count());

        for (var index = 0;
             index < 16;
             index++)
        {
            using var accepted =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        refreshToken =
                            firstGenerationTokens[index]
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                accepted.StatusCode);
        }

        using var overLimit =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        firstGenerationTokens[16]
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            overLimit.StatusCode);

        using var replayFirst =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        firstGenerationTokens[0]
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayFirst.StatusCode);

        await using var verificationScope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    FullWorthDbContext>();

        var familyCount =
            await dbContext.UserTokens
                .CountAsync(
                    token =>
                        token.LoginProvider ==
                            "FullWorth.RefreshFamily");

        Assert.Equal(
            16,
            familyCount);
    }

    [Fact]
    public async Task SecurityStampChange_PrunesInvalidatedRefreshFamilies()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"refresh-stamp-prune-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        var originalLogin =
            await LoginAsync(
                client,
                email);

        using var establishFamilyResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        originalLogin.RefreshToken
                });

        establishFamilyResponse.EnsureSuccessStatusCode();

        var establishedFamily =
            await establishFamilyResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            establishedFamily);

        await using (
            var stampScope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                stampScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    email);

            Assert.NotNull(
                user);

            var stampResult =
                await userManager.UpdateSecurityStampAsync(
                    user!);

            Assert.True(
                stampResult.Succeeded);
        }

        using var staleRefreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        establishedFamily!.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            staleRefreshResponse.StatusCode);

        var newLogin =
            await LoginAsync(
                client,
                email);

        using var newFamilyResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        newLogin.RefreshToken
                });

        newFamilyResponse.EnsureSuccessStatusCode();

        await using var verificationScope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    FullWorthDbContext>();

        var familyRows =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.LoginProvider ==
                            "FullWorth.RefreshFamily")
                .ToListAsync();

        Assert.Single(
            familyRows);
    }

    [Fact]
    public async Task SecurityStampChange_PrunesOldFamiliesBeforeApplyingFamilyLimit()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"refresh-family-stamp-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        var originalLogin =
            await LoginAsync(
                client,
                email);

        var bearerOptions =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        BearerTokenOptions>>()
                .Get(
                    IdentityConstants
                        .BearerScheme);

        var originalTicket =
            bearerOptions
                .RefreshTokenProtector
                .Unprotect(
                    originalLogin.RefreshToken);

        Assert.NotNull(
            originalTicket);

        var originalFamilyTokens =
            Enumerable
                .Range(
                    0,
                    16)
                .Select(
                    _ =>
                        bearerOptions
                            .RefreshTokenProtector
                            .Protect(
                                originalTicket!))
                .ToArray();

        foreach (var familyToken in
                 originalFamilyTokens)
        {
            using var accepted =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        refreshToken =
                            familyToken
                    });

            Assert.Equal(
                HttpStatusCode.OK,
                accepted.StatusCode);
        }

        await using (
            var securityScope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                securityScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    email);

            Assert.NotNull(
                user);

            var stampResult =
                await userManager.UpdateSecurityStampAsync(
                    user!);

            Assert.True(
                stampResult.Succeeded,
                FormatErrors(
                    stampResult));
        }

        var postSecurityChangeLogin =
            await LoginAsync(
                client,
                email);

        using var refreshed =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        postSecurityChangeLogin.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.OK,
            refreshed.StatusCode);

        await using var verificationScope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    FullWorthDbContext>();

        var currentFamilies =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.LoginProvider ==
                            "FullWorth.RefreshFamily")
                .ToListAsync();

        Assert.Single(
            currentFamilies);
    }

    [Fact]
    public async Task RefreshTokenRotation_KeepsOneReplayMarkerPerLoginFamily()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"refresh-family-{Guid.NewGuid():N}@fullworth.local";

        await RegisterAsync(
            client,
            email);

        var loginResult =
            await LoginAsync(
                client,
                email);

        var currentRefreshToken =
            loginResult.RefreshToken;

        for (var rotation = 0;
             rotation < 4;
             rotation++)
        {
            using var refreshResponse =
                await client.PostAsJsonAsync(
                    "/api/auth/refresh",
                    new
                    {
                        refreshToken =
                            currentRefreshToken
                    });

            refreshResponse.EnsureSuccessStatusCode();

            var refreshed =
                await refreshResponse.Content
                    .ReadFromJsonAsync<LoginResult>();

            Assert.NotNull(
                refreshed);

            currentRefreshToken =
                refreshed!.RefreshToken;
        }

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    FullWorth.API.Data.FullWorthDbContext>();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    Microsoft.AspNetCore.Identity.UserManager<
                        FullWorth.API.Data.Entities.ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(
            user);

        var familyMarkers =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.UserId == user!.Id &&
                        token.LoginProvider ==
                            "FullWorth.RefreshFamily")
                .ToListAsync();

        Assert.Single(
            familyMarkers);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutStaffRole_RemainsForbidden()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var adminResponse =
            await client.GetAsync(
                "/api/admin/access-keys");

        Assert.Equal(
            HttpStatusCode.Forbidden,
            adminResponse.StatusCode);
    }

    private static async Task RegisterAsync(
        HttpClient client,
        string email)
    {
        using var registerResponse =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password = Password,
                    acceptedTermsAndPrivacy = true,
                    legalTermsVersion = FullWorthLegalDocuments.CurrentVersion
                });

        registerResponse.EnsureSuccessStatusCode();
    }

    private static async Task<LoginResult> LoginAsync(
        HttpClient client,
        string email)
    {
        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password = Password
                });

        loginResponse.EnsureSuccessStatusCode();

        var loginResult =
            await loginResponse.Content
                .ReadFromJsonAsync<LoginResult>();

        Assert.NotNull(
            loginResult);

        Assert.False(
            string.IsNullOrWhiteSpace(
                loginResult!.AccessToken));

        Assert.False(
            string.IsNullOrWhiteSpace(
                loginResult.RefreshToken));

        return loginResult;
    }

    private static async Task AssignOwnerRoleAsync(
        FullWorthApiFactory factory,
        string email)
    {
        using var scope =
            factory.Services.CreateScope();

        var roleManager =
            scope.ServiceProvider
                .GetRequiredService<
                    RoleManager<IdentityRole<Guid>>>();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        if (!await roleManager.RoleExistsAsync(
                FullWorthRoles.Owner))
        {
            var createRoleResult =
                await roleManager.CreateAsync(
                    new IdentityRole<Guid>(
                        FullWorthRoles.Owner));

            Assert.True(
                createRoleResult.Succeeded,
                FormatErrors(
                    createRoleResult));
        }

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(
            user);

        var addRoleResult =
            await userManager.AddToRoleAsync(
                user!,
                FullWorthRoles.Owner);

        Assert.True(
            addRoleResult.Succeeded,
            FormatErrors(
                addRoleResult));
    }

    private static string FormatErrors(
        IdentityResult result)
    {
        return string.Join(
            "; ",
            result.Errors.Select(
                error =>
                    $"{error.Code}: {error.Description}"));
    }

    private sealed record LoginResult(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}

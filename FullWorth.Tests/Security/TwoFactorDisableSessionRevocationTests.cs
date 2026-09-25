using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class TwoFactorDisableSessionRevocationTests
{
    private const string Password =
        "FullWorth!Tests123";

    [Fact]
    public async Task Disable_ValidReauthentication_RotatesStampAndInvalidatesExistingRefreshSessions()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var bootstrapSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            bootstrapSession);

        var sharedKey =
            await SetupAndEnableTwoFactorAsync(
                client);

        var firstSession =
            await LoginWithTwoFactorAsync(
                client,
                bootstrapSession.Email,
                sharedKey);

        var secondSession =
            await LoginWithTwoFactorAsync(
                client,
                bootstrapSession.Email,
                sharedKey);

        var originalStamp =
            await GetSecurityStampAsync(
                factory,
                bootstrapSession.Email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                firstSession.AccessToken);

        using var disableResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/disable",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        CreateAuthenticatorCode(
                            sharedKey)
                });

        Assert.Equal(
            HttpStatusCode.NoContent,
            disableResponse.StatusCode);

        var updatedStamp =
            await GetSecurityStampAsync(
                factory,
                bootstrapSession.Email);

        Assert.NotEqual(
            originalStamp,
            updatedStamp);

        Assert.False(
            await GetTwoFactorEnabledAsync(
                factory,
                bootstrapSession.Email));

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
    public async Task Disable_InvalidAuthenticatorCode_PreservesMfaStampAndRefreshSession()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var bootstrapSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            bootstrapSession);

        var sharedKey =
            await SetupAndEnableTwoFactorAsync(
                client);

        var activeSession =
            await LoginWithTwoFactorAsync(
                client,
                bootstrapSession.Email,
                sharedKey);

        var originalStamp =
            await GetSecurityStampAsync(
                factory,
                bootstrapSession.Email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                activeSession.AccessToken);

        using var disableResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/disable",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        "invalid"
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            disableResponse.StatusCode);

        Assert.Equal(
            originalStamp,
            await GetSecurityStampAsync(
                factory,
                bootstrapSession.Email));

        Assert.True(
            await GetTwoFactorEnabledAsync(
                factory,
                bootstrapSession.Email));

        client.DefaultRequestHeaders.Authorization =
            null;

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        activeSession.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.OK,
            refreshResponse.StatusCode);
    }

    private static async Task<string>
        SetupAndEnableTwoFactorAsync(
            HttpClient client)
    {
        using var setupResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/setup",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.OK,
            setupResponse.StatusCode);

        var setup =
            await setupResponse.Content
                .ReadFromJsonAsync<
                    SetupResponse>();

        Assert.NotNull(setup);

        using var enableResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/enable",
                new
                {
                    currentPassword =
                        Password,

                    authenticatorCode =
                        CreateAuthenticatorCode(
                            setup.SharedKey)
                });

        Assert.Equal(
            HttpStatusCode.OK,
            enableResponse.StatusCode);

        return setup.SharedKey;
    }

    private static async Task<TestUserSession>
        LoginWithTwoFactorAsync(
            HttpClient client,
            string email,
            string sharedKey)
    {
        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password =
                        Password,

                    twoFactorCode =
                        CreateAuthenticatorCode(
                            sharedKey)
                });

        response.EnsureSuccessStatusCode();

        var result =
            await response.Content
                .ReadFromJsonAsync<
                    LoginResponse>();

        Assert.NotNull(result);

        return new TestUserSession(
            email,
            result.AccessToken,
            result.RefreshToken);
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

        Assert.NotNull(user);

        return await userManager
            .GetSecurityStampAsync(
                user!);
    }

    private static async Task<bool>
        GetTwoFactorEnabledAsync(
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

        Assert.NotNull(user);

        return await userManager
            .GetTwoFactorEnabledAsync(
                user!);
    }

    private static string
        CreateAuthenticatorCode(
            string key)
    {
        const string alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var bytes =
            new List<byte>();

        var buffer = 0;
        var bits = 0;

        foreach (var character in key)
        {
            buffer =
                (buffer << 5) |
                alphabet.IndexOf(character);

            bits += 5;

            if (bits < 8)
            {
                continue;
            }

            bits -= 8;
            bytes.Add(
                (byte)(buffer >> bits));
        }

        Span<byte> counter =
            stackalloc byte[8];

        BinaryPrimitives.WriteInt64BigEndian(
            counter,
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds() /
            30);

        var hash =
            HMACSHA1.HashData(
                bytes.ToArray(),
                counter);

        var offset =
            hash[^1] & 15;

        var value =
            BinaryPrimitives.ReadInt32BigEndian(
                hash.AsSpan(
                    offset,
                    4)) &
            int.MaxValue;

        return (value % 1_000_000)
            .ToString(
                "D6",
                CultureInfo.InvariantCulture);
    }

    private sealed record SetupResponse(
        string SharedKey,
        string OtpAuthUri);

    private sealed record LoginResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}

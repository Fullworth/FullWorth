using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class TwoFactorDisableSecurityTests
{
    private const string Password =
        "FullWorth!Tests123";

    [Fact]
    public async Task Disable_ValidReauthentication_InvalidatesRefreshTokenIssuedAfterMfaWasEnabled()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var initialSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var sharedKey =
            await EnableTwoFactorAsync(
                client,
                initialSession);

        var mfaSession =
            await LoginWithTwoFactorAsync(
                client,
                initialSession.Email,
                sharedKey);

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                initialSession.Email);

        TestUserAuthentication.Authorize(
            client,
            mfaSession);

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

        await using (
            var scope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    initialSession.Email);

            Assert.NotNull(user);
            Assert.False(
                await userManager.GetTwoFactorEnabledAsync(
                    user!));

            Assert.NotEqual(
                originalSecurityStamp,
                await userManager.GetSecurityStampAsync(
                    user!));
        }

        client.DefaultRequestHeaders.Authorization =
            null;

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        mfaSession.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            refreshResponse.StatusCode);
    }

    [Fact]
    public async Task Disable_InvalidAuthenticatorCode_PreservesMfaStampAndRefreshToken()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var initialSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var sharedKey =
            await EnableTwoFactorAsync(
                client,
                initialSession);

        var mfaSession =
            await LoginWithTwoFactorAsync(
                client,
                initialSession.Email,
                sharedKey);

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                initialSession.Email);

        TestUserAuthentication.Authorize(
            client,
            mfaSession);

        var currentCode =
            CreateAuthenticatorCode(
                sharedKey);

        var invalidCode =
            string.Equals(
                currentCode,
                "000000",
                StringComparison.Ordinal)
                ? "000001"
                : "000000";

        using var disableResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/disable",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        invalidCode
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            disableResponse.StatusCode);

        await using (
            var scope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    initialSession.Email);

            Assert.NotNull(user);
            Assert.True(
                await userManager.GetTwoFactorEnabledAsync(
                    user!));

            Assert.Equal(
                originalSecurityStamp,
                await userManager.GetSecurityStampAsync(
                    user!));
        }

        client.DefaultRequestHeaders.Authorization =
            null;

        using var refreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        mfaSession.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.OK,
            refreshResponse.StatusCode);
    }

    private static async Task<string> EnableTwoFactorAsync(
        HttpClient client,
        TestUserSession initialSession)
    {
        TestUserAuthentication.Authorize(
            client,
            initialSession);

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
                .ReadFromJsonAsync<SetupResponse>();

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
        client.DefaultRequestHeaders.Authorization =
            null;

        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password =
                        Password,

                    twoFactorCode =
                        CreateAuthenticatorCode(
                            sharedKey),

                    twoFactorRecoveryCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.OK,
            loginResponse.StatusCode);

        var login =
            await loginResponse.Content
                .ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(login);
        Assert.False(
            string.IsNullOrWhiteSpace(
                login.AccessToken));
        Assert.False(
            string.IsNullOrWhiteSpace(
                login.RefreshToken));

        return new TestUserSession(
            email,
            login.AccessToken,
            login.RefreshToken);
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

        return await userManager.GetSecurityStampAsync(
            user!);
    }

    private sealed record SetupResponse(
        string SharedKey,
        string OtpAuthUri);

    private sealed record LoginResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);

    private static string CreateAuthenticatorCode(
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
                hash.AsSpan(offset, 4)) &
            int.MaxValue;

        return (value % 1000000)
            .ToString(
                "D6",
                CultureInfo.InvariantCulture);
    }
}

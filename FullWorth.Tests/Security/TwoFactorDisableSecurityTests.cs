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
    public async Task Disable_ValidReauthentication_DisablesMfaAndInvalidatesExistingRefreshTokens()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var firstSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var secondSession =
            await TestUserAuthentication.LoginAsync(
                client,
                firstSession.Email);

        TestUserAuthentication.Authorize(
            client,
            firstSession);

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

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                firstSession.Email);

        using var disableResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/disable",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        CreateAuthenticatorCode(
                            setup.SharedKey)
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
                    firstSession.Email);

            Assert.NotNull(user);
            Assert.False(
                await userManager.GetTwoFactorEnabledAsync(
                    user!));

            var updatedSecurityStamp =
                await userManager.GetSecurityStampAsync(
                    user!);

            Assert.NotEqual(
                originalSecurityStamp,
                updatedSecurityStamp);
        }

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
    public async Task Disable_InvalidAuthenticatorCode_DoesNotDisableMfaOrRevokeRefreshToken()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            session);

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

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                session.Email);

        var currentCode =
            CreateAuthenticatorCode(
                setup.SharedKey);

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
                    session.Email);

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

        Assert.NotNull(user);

        return await userManager.GetSecurityStampAsync(
            user!);
    }

    private sealed record SetupResponse(
        string SharedKey,
        string OtpAuthUri);

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

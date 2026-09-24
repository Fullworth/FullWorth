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

public sealed class RecoveryCodeAuthorizationTests
{
    private const string TestPassword =
        "FullWorth!Tests123";

    [Fact]
    public async Task Regenerate_AnonymousUser_IsRejected()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/recovery-codes",
                new
                {
                    currentPassword =
                        "x",

                    twoFactorCode =
                        "000000"
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task Regenerate_Success_RotatesSecurityStampAndRejectsPriorRefreshToken()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var initialSession =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        string authenticatorKey;

        await using (
            var setupScope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                setupScope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    initialSession.Email);

            Assert.NotNull(
                user);

            var resetResult =
                await userManager.ResetAuthenticatorKeyAsync(
                    user!);

            Assert.True(
                resetResult.Succeeded);

            var enableResult =
                await userManager.SetTwoFactorEnabledAsync(
                    user!,
                    true);

            Assert.True(
                enableResult.Succeeded);

            authenticatorKey =
                await userManager.GetAuthenticatorKeyAsync(
                    user!) ??
                throw new InvalidOperationException(
                    "The test user did not receive an authenticator key.");
        }

        var authenticatorCode =
            GenerateAuthenticatorCode(
                authenticatorKey);

        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email =
                        initialSession.Email,

                    password =
                        TestPassword,

                    twoFactorCode =
                        authenticatorCode,

                    twoFactorRecoveryCode =
                        (string?)null
                });

        loginResponse.EnsureSuccessStatusCode();

        var currentSession =
            await loginResponse.Content
                .ReadFromJsonAsync<LoginTokenResponse>();

        Assert.NotNull(
            currentSession);

        var originalSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                initialSession.Email);

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                currentSession!.AccessToken);

        using var regenerateResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/recovery-codes",
                new
                {
                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        GenerateAuthenticatorCode(
                            authenticatorKey)
                });

        Assert.Equal(
            HttpStatusCode.OK,
            regenerateResponse.StatusCode);

        var regenerated =
            await regenerateResponse.Content
                .ReadFromJsonAsync<
                    RecoveryCodesResponse>();

        Assert.NotNull(
            regenerated);

        Assert.Equal(
            10,
            regenerated!.RecoveryCodes.Count);

        var updatedSecurityStamp =
            await GetSecurityStampAsync(
                factory,
                initialSession.Email);

        Assert.NotEqual(
            originalSecurityStamp,
            updatedSecurityStamp);

        client.DefaultRequestHeaders.Authorization =
            null;

        using var staleRefreshResponse =
            await client.PostAsJsonAsync(
                "/api/auth/refresh",
                new
                {
                    refreshToken =
                        currentSession.RefreshToken
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            staleRefreshResponse.StatusCode);
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

        return await userManager.GetSecurityStampAsync(
            user!);
    }

    private static string GenerateAuthenticatorCode(
        string base32Key)
    {
        var keyBytes =
            DecodeBase32(
                base32Key);

        var timeStep =
            (ulong)(
                DateTimeOffset.UtcNow
                    .ToUnixTimeSeconds() /
                30);

        Span<byte> counter =
            stackalloc byte[8];

        BinaryPrimitives.WriteUInt64BigEndian(
            counter,
            timeStep);

        using var hmac =
            new HMACSHA1(
                keyBytes);

        var hash =
            hmac.ComputeHash(
                counter.ToArray());

        var offset =
            hash[^1] & 0x0f;

        var binaryCode =
            ((hash[offset] & 0x7f) << 24) |
            (hash[offset + 1] << 16) |
            (hash[offset + 2] << 8) |
            hash[offset + 3];

        var code =
            binaryCode %
            1_000_000;

        return code.ToString(
            "D6",
            CultureInfo.InvariantCulture);
    }

    private static byte[] DecodeBase32(
        string value)
    {
        const string alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        var normalized =
            value
                .Trim()
                .TrimEnd('=')
                .ToUpperInvariant();

        var bytes =
            new List<byte>();

        var buffer =
            0;

        var bitsLeft =
            0;

        foreach (var character in normalized)
        {
            var digit =
                alphabet.IndexOf(
                    character);

            if (digit < 0)
            {
                throw new InvalidOperationException(
                    "The authenticator key contains an invalid Base32 character.");
            }

            buffer =
                (buffer << 5) |
                digit;

            bitsLeft +=
                5;

            if (bitsLeft < 8)
            {
                continue;
            }

            bitsLeft -=
                8;

            bytes.Add(
                (byte)(
                    buffer >>
                    bitsLeft));

            buffer =
                bitsLeft == 0
                    ? 0
                    : buffer &
                      ((1 << bitsLeft) - 1);
        }

        return bytes.ToArray();
    }

    private sealed record LoginTokenResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);

    private sealed record RecoveryCodesResponse(
        IReadOnlyList<string> RecoveryCodes);
}

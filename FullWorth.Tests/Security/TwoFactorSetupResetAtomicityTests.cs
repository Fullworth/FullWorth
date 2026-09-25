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

public sealed class TwoFactorSetupResetAtomicityTests
{
    private const string Password =
        "FullWorth!Tests123";

    [Fact]
    public async Task Setup_AlreadyEnabled_IsRejectedWithoutChangingMfaState()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var sharedKey =
            await SetupAndEnableTwoFactorAsync(
                client,
                session);

        var before =
            await GetMfaStateAsync(
                factory,
                session.Email);

        Assert.True(before.TwoFactorEnabled);
        Assert.Equal(sharedKey, before.AuthenticatorKey);
        Assert.Equal(10, before.RecoveryCodesLeft);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/setup",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        CreateAuthenticatorCode(
                            sharedKey)
                });

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        var after =
            await GetMfaStateAsync(
                factory,
                session.Email);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Reset_ValidReauthentication_CommitsNewKeyDisabledMfaAndSessionRevocationTogether()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var bootstrapSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var oldSharedKey =
            await SetupAndEnableTwoFactorAsync(
                client,
                bootstrapSession);

        var activeSession =
            await LoginWithTwoFactorAsync(
                client,
                bootstrapSession.Email,
                oldSharedKey);

        var before =
            await GetMfaStateAsync(
                factory,
                bootstrapSession.Email);

        TestUserAuthentication.Authorize(
            client,
            activeSession);

        using var resetResponse =
            await client.PostAsJsonAsync(
                "/api/account/security/two-factor/reset",
                new
                {
                    currentPassword =
                        Password,

                    twoFactorCode =
                        CreateAuthenticatorCode(
                            oldSharedKey)
                });

        Assert.Equal(
            HttpStatusCode.OK,
            resetResponse.StatusCode);

        var reset =
            await resetResponse.Content
                .ReadFromJsonAsync<SetupResponse>();

        Assert.NotNull(reset);
        Assert.False(
            string.IsNullOrWhiteSpace(
                reset.SharedKey));
        Assert.NotEqual(
            oldSharedKey,
            reset.SharedKey);

        var after =
            await GetMfaStateAsync(
                factory,
                bootstrapSession.Email);

        Assert.False(after.TwoFactorEnabled);
        Assert.Equal(
            reset.SharedKey,
            after.AuthenticatorKey);
        Assert.NotEqual(
            before.SecurityStamp,
            after.SecurityStamp);

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
            HttpStatusCode.Unauthorized,
            refreshResponse.StatusCode);
    }

    [Fact]
    public void SetupAndReset_SourcePreservesSinglePersistedTransitionBoundaries()
    {
        var source =
            File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "FullWorth.API",
                    "Controllers",
                    "AccountSecurityController.cs"));

        var setup =
            SliceMethod(
                source,
                "public async Task<ActionResult<TwoFactorSetupResponse>> SetupTwoFactor(",
                "[HttpPost(\"two-factor/enable\")]");

        Assert.Contains(
            "GetTwoFactorEnabledAsync",
            setup,
            StringComparison.Ordinal);

        Assert.Contains(
            "StatusCodes.Status409Conflict",
            setup,
            StringComparison.Ordinal);

        Assert.Contains(
            "ResetAuthenticatorKeyAsync",
            setup,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "SetTwoFactorEnabledAsync",
            setup,
            StringComparison.Ordinal);

        var reset =
            SliceMethod(
                source,
                "public async Task<ActionResult<TwoFactorSetupResponse>> ResetTwoFactor(",
                "private async Task<ApplicationUser?> GetCurrentUserAsync");

        Assert.Contains(
            "IUserTwoFactorStore<ApplicationUser>",
            reset,
            StringComparison.Ordinal);

        Assert.Contains(
            "IUserAuthenticatorKeyStore<ApplicationUser>",
            reset,
            StringComparison.Ordinal);

        Assert.Contains(
            "IUserSecurityStampStore<ApplicationUser>",
            reset,
            StringComparison.Ordinal);

        Assert.Contains(
            "await userManager.UpdateAsync(user)",
            reset,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "userManager.SetTwoFactorEnabledAsync",
            reset,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "userManager.ResetAuthenticatorKeyAsync",
            reset,
            StringComparison.Ordinal);
    }

    private static async Task<string>
        SetupAndEnableTwoFactorAsync(
            HttpClient client,
            TestUserSession session)
    {
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
                            sharedKey),

                    twoFactorRecoveryCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var result =
            await response.Content
                .ReadFromJsonAsync<LoginResponse>();

        Assert.NotNull(result);

        return new TestUserSession(
            email,
            result.AccessToken,
            result.RefreshToken);
    }

    private static async Task<MfaState>
        GetMfaStateAsync(
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

        return new MfaState(
            await userManager.GetTwoFactorEnabledAsync(
                user!),
            await userManager.GetAuthenticatorKeyAsync(
                user!),
            await userManager.GetSecurityStampAsync(
                user!),
            await userManager.CountRecoveryCodesAsync(
                user!));
    }

    private static string SliceMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        var start =
            source.IndexOf(
                startMarker,
                StringComparison.Ordinal);

        Assert.True(
            start >= 0,
            $"Missing method start marker: {startMarker}");

        var end =
            source.IndexOf(
                endMarker,
                start,
                StringComparison.Ordinal);

        Assert.True(
            end > start,
            $"Missing method end marker: {endMarker}");

        return source[start..end];
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "FullWorth.slnx")) &&
                Directory.Exists(
                    Path.Combine(
                        directory.FullName,
                        "FullWorth.API")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the FullWorth repository root.");
    }

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

    private sealed record MfaState(
        bool TwoFactorEnabled,
        string? AuthenticatorKey,
        string SecurityStamp,
        int RecoveryCodesLeft);
}

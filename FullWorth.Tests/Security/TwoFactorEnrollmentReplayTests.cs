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

public sealed class TwoFactorEnrollmentReplayTests
{
    private const string Password = "FullWorth!Tests123";

    [Fact]
    public async Task Enable_ReplayedEnrollment_PreservesIssuedRecoveryCodesAndSecurityStamp()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);

        using var setupResponse = await client.PostAsJsonAsync(
            "/api/account/security/two-factor/setup",
            new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = await setupResponse.Content.ReadFromJsonAsync<SetupResponse>();
        Assert.NotNull(setup);

        var enableRequest = new
        {
            currentPassword = Password,
            authenticatorCode = CreateAuthenticatorCode(setup.SharedKey)
        };
        using var firstResponse = await client.PostAsJsonAsync(
            "/api/account/security/two-factor/enable", enableRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstCodes = await firstResponse.Content.ReadFromJsonAsync<RecoveryCodesResponse>();
        Assert.NotNull(firstCodes);
        Assert.Equal(10, firstCodes.RecoveryCodes.Length);

        string originalStamp;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByEmailAsync(session.Email))!;
            Assert.True(await manager.GetTwoFactorEnabledAsync(user));
            originalStamp = await manager.GetSecurityStampAsync(user);
        }

        using var replayResponse = await client.PostAsJsonAsync(
            "/api/account/security/two-factor/enable", enableRequest);
        Assert.Equal(HttpStatusCode.Conflict, replayResponse.StatusCode);
        Assert.DoesNotContain("recoveryCodes", await replayResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no-store", replayResponse.Headers.CacheControl!.ToString());

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByEmailAsync(session.Email))!;
            Assert.True(await manager.GetTwoFactorEnabledAsync(user));
            Assert.Equal(originalStamp, await manager.GetSecurityStampAsync(user));
            Assert.Equal(10, await manager.CountRecoveryCodesAsync(user));
            foreach (var code in firstCodes.RecoveryCodes)
                Assert.True((await manager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded);
            Assert.False((await manager.RedeemTwoFactorRecoveryCodeAsync(user, firstCodes.RecoveryCodes[0])).Succeeded);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enable_InvalidProof_DoesNotEnableOrIssueRecoveryCodes(bool wrongPassword)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);
        using var setupResponse = await client.PostAsJsonAsync(
            "/api/account/security/two-factor/setup", new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setup = (await setupResponse.Content.ReadFromJsonAsync<SetupResponse>())!;
        using var response = await client.PostAsJsonAsync(
            "/api/account/security/two-factor/enable",
            new
            {
                currentPassword = wrongPassword ? "WrongPassword!123" : Password,
                authenticatorCode = wrongPassword ? CreateAuthenticatorCode(setup.SharedKey) : "invalid"
            });
        Assert.Equal(wrongPassword ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await manager.FindByEmailAsync(session.Email))!;
        Assert.False(await manager.GetTwoFactorEnabledAsync(user));
        Assert.Equal(0, await manager.CountRecoveryCodesAsync(user));
    }

    private sealed record SetupResponse(string SharedKey, string OtpAuthUri);
    private sealed record RecoveryCodesResponse(string[] RecoveryCodes);

    private static string CreateAuthenticatorCode(string key, int offsetSeconds = 0)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in key)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes.Add((byte)(buffer >> bits));
            }
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.AddSeconds(offsetSeconds).ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (value % 1000000).ToString("D6", CultureInfo.InvariantCulture);
    }

}

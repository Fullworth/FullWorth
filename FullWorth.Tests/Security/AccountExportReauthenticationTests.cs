using System.Net;
using System.Net.Http.Json;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class AccountExportReauthenticationTests
    : IClassFixture<FullWorthApiFactory>
{
    private readonly FullWorthApiFactory _factory;

    public AccountExportReauthenticationTests(FullWorthApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExportAccountData_TwoFactorRequiresCurrentAuthenticatorProof()
    {
        using var client = _factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);
        string key;
        string recoveryCode;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByEmailAsync(session.Email))!;
            Assert.True((await manager.ResetAuthenticatorKeyAsync(user)).Succeeded);
            key = (await manager.GetAuthenticatorKeyAsync(user))!;
            Assert.True((await manager.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            recoveryCode = (await manager.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        }

        foreach (var code in new string?[] { null, "not-a-code", recoveryCode, CreateAuthenticatorCode(key, -600) })
        {
            using var denied = await client.PostAsJsonAsync("/api/account/export",
                new { currentPassword = "FullWorth!Tests123", twoFactorCode = code });
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.DoesNotContain(session.Email, await denied.Content.ReadAsStringAsync());
        }

        var currentCode = CreateAuthenticatorCode(key);
        using var allowed = await client.PostAsJsonAsync("/api/account/export",
            new { currentPassword = "FullWorth!Tests123", twoFactorCode = currentCode.Insert(3, " - ") });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains(session.Email, await allowed.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExportAccountData_RejectedProofIsRateLimited()
    {
        using var client = _factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var response = await client.PostAsJsonAsync("/api/account/export",
                new { currentPassword = "WrongPassword!123" });
            Assert.Equal(attempt < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests,
                response.StatusCode);
        }
    }

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

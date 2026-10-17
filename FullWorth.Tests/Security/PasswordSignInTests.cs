using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using FullWorth.API;

namespace FullWorth.Tests.Security;

public sealed class PasswordSignInTests
{
    private const string Password = "FullWorth!Tests123";

    [Fact]
    public async Task NonMfaAccountSignsInNormally()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var account = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var response = await client.PostAsJsonAsync("/api/auth/password/start", new { email = account.Email, password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("accessToken", out _));
    }

    [Fact]
    public async Task PasswordProofDoesNotGrantAccessAndCodeCompletionNeedsNoPassword()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var account = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var key = await EnableMfa(factory, account.Email);
        var start = await Start(client, account.Email);
        Assert.False(start.TryGetProperty("accessToken", out _));
        Assert.False(start.TryGetProperty("refreshToken", out _));
        Assert.False(start.GetProperty("canUsePasskey").GetBoolean());
        Assert.DoesNotContain(Password, start.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/bill-streams")).StatusCode);

        var body = new { loginId = Id(start), twoFactorCode = Code(key) };
        var success = await client.PostAsJsonAsync("/api/auth/password/complete", body);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.True((await success.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("accessToken", out _));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/password/complete", body)).StatusCode);
    }

    [Fact]
    public async Task InvalidCodeRotatesProofWithoutExtendingExpiryOrAllowingUnlimitedRetries()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var account = await TestUserAuthentication.RegisterAndLoginAsync(client);
        await EnableMfa(factory, account.Email);
        var start = await Start(client, account.Email);
        var original = Id(start);
        var expiry = start.GetProperty("expiresAt").GetDateTimeOffset();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var failed = await client.PostAsJsonAsync("/api/auth/password/complete",
                new { loginId = Id(start), twoFactorCode = "invalid" });
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
            var next = await failed.Content.ReadFromJsonAsync<JsonElement>();
            if (attempt < 2)
            {
                Assert.NotEqual(Id(start), Id(next));
                Assert.Equal(expiry, next.GetProperty("expiresAt").GetDateTimeOffset());
            }
            else Assert.False(next.TryGetProperty("loginId", out _));
            start = next;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/auth/password/complete", new { loginId = original, twoFactorCode = "123456" })).StatusCode);
    }

    [Theory]
    [InlineData("stamp")]
    [InlineData("inactive")]
    [InlineData("locked")]
    [InlineData("mfa-disabled")]
    public async Task ChangedAccountCannotFinishOldPasswordProof(string change)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var account = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var key = await EnableMfa(factory, account.Email);
        var start = await Start(client, account.Email);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(account.Email))!;
            if (change == "stamp") await users.UpdateSecurityStampAsync(user);
            if (change == "inactive") { user.IsActive = false; await users.UpdateAsync(user); }
            if (change == "locked") await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(10));
            if (change == "mfa-disabled") await users.SetTwoFactorEnabledAsync(user, false);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(
            "/api/auth/password/complete", new { loginId = Id(start), twoFactorCode = Code(key) })).StatusCode);
    }

    [Fact]
    public async Task RecoveryCodeIsAnAlternativeAndCannotBeReused()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var account = await TestUserAuthentication.RegisterAndLoginAsync(client);
        await EnableMfa(factory, account.Email);
        string recovery;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(account.Email))!;
            recovery = (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
        }
        var first = await Start(client, account.Email);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/password/complete",
            new { loginId = Id(first), recoveryCode = recovery })).StatusCode);
        var second = await Start(client, account.Email);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/password/complete",
            new { loginId = Id(second), recoveryCode = recovery })).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasskeyCannotReplacePasswordProofOrUseAnotherAccountsKey(bool wrongOwner)
    {
        await using var factory = new FullWorthApiFactory();
        await using var enabled = EnablePasskeys(factory);
        using var client = enabled.CreateClient();
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, owner);
        using var ownerKey = new VirtualKey();
        await Enroll(client, ownerKey);
        client.DefaultRequestHeaders.Authorization = null;
        var other = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, other);
        using var otherKey = new VirtualKey();
        await Enroll(client, otherKey);
        client.DefaultRequestHeaders.Authorization = null;
        await EnableMfa(enabled, owner.Email);
        var pending = await Start(client, owner.Email);
        var options = await Begin(client, "/api/auth/password/passkey-options", new { loginId = Id(pending) });
        var response = await client.PostAsJsonAsync("/api/auth/password/complete", new
        {
            loginId = wrongOwner ? Id(pending) : new string('x', 43),
            ceremonyId = options.GetProperty("ceremonyId").GetString(),
            credentialJson = (wrongOwner ? otherKey : ownerKey).Assert(options.GetProperty("options"))
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("no-uv")]
    [InlineData("wrong-origin")]
    [InlineData("wrong-proof")]
    [InlineData("removed")]
    public async Task RegisteredUserVerifiedPasskeyCanFinishOnlyItsBoundPasswordSignIn(string scenario)
    {
        await using var factory = new FullWorthApiFactory();
        await using var enabled = EnablePasskeys(factory);
        using var client = enabled.CreateClient();
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, owner);
        using var key = new VirtualKey();
        await Enroll(client, key);
        client.DefaultRequestHeaders.Authorization = null;
        await EnableMfa(enabled, owner.Email);
        var pending = await Start(client, owner.Email);
        Assert.True(pending.GetProperty("canUsePasskey").GetBoolean());
        var options = await Begin(client, "/api/auth/password/passkey-options", new { loginId = Id(pending) });
        if (scenario == "removed")
        {
            await using var scope = enabled.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(owner.Email))!;
            var registered = (await users.GetPasskeysAsync(user)).Single();
            Assert.True((await users.RemovePasskeyAsync(user, registered.CredentialId)).Succeeded);
        }
        var submittedProof = scenario == "wrong-proof" ? await Start(client, owner.Email) : pending;
        var body = new { loginId = Id(submittedProof),
            ceremonyId = options.GetProperty("ceremonyId").GetString(),
            credentialJson = key.Assert(options.GetProperty("options"),
                verified: scenario != "no-uv",
                origin: scenario == "wrong-origin" ? "https://evil.example" : "https://fullworth.org") };
        var response = await client.PostAsJsonAsync("/api/auth/password/complete", body);
        Assert.Equal(scenario == "valid" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/password/complete", body)).StatusCode);
    }

    private static WebApplicationFactory<ApiAssemblyMarker> EnablePasskeys(FullWorthApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Passkeys:Enabled"] = "true" })));

    private static async Task<string> EnableMfa(WebApplicationFactory<ApiAssemblyMarker> factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        return (await users.GetAuthenticatorKeyAsync(user))!;
    }

    private static async Task<JsonElement> Start(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/password/start", new { email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static string Id(JsonElement value) => value.GetProperty("loginId").GetString()!;
    private static async Task<JsonElement> Begin(HttpClient client, string path, object request)
    {
        var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Enroll(HttpClient client, VirtualKey key)
    {
        var options = await Begin(client, "/api/account/security/passkeys/options", new { currentPassword = Password });
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/account/security/passkeys/register",
            new { ceremonyId = options.GetProperty("ceremonyId").GetString(),
                credentialJson = key.Register(options.GetProperty("options")) })).StatusCode);
    }

    internal static string Code(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0; var bits = 0;
        foreach (var character in key)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits < 8) continue;
            bits -= 8; bytes.Add((byte)(buffer >> bits));
        }
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes.ToArray(), counter);
        var offset = hash[^1] & 15;
        var value = BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & int.MaxValue;
        return (value % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }
    private sealed class VirtualKey : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _id = RandomNumberGenerator.GetBytes(32);
        private string _userHandle = "";

        public string Register(JsonElement options, string origin = "https://fullworth.org", bool verified = true)
        {
            _userHandle = options.GetProperty("user").GetProperty("id").GetString()!;
            var clientData = ClientData("webauthn.create", options, origin);
            var p = _key.ExportParameters(false);
            var cose = new byte[] { 0xa5, 1, 2, 3, 0x26, 0x20, 1, 0x21, 0x58, 0x20 }
                .Concat(p.Q.X!).Concat(new byte[] { 0x22, 0x58, 0x20 }).Concat(p.Q.Y!).ToArray();
            var auth = AuthData((byte)(verified ? 0x45 : 0x41), 0)
                .Concat(new byte[16]).Concat(new byte[] { 0, 32 }).Concat(_id).Concat(cose).ToArray();
            using var stream = new MemoryStream();
            stream.WriteByte(0xa3);
            Text(stream, "fmt"); Text(stream, "none");
            Text(stream, "authData"); ByteString(stream, auth);
            Text(stream, "attStmt"); stream.WriteByte(0xa0);
            return JsonSerializer.Serialize(new
            {
                id = B64(_id), rawId = B64(_id), type = "public-key",
                response = new { clientDataJSON = B64(clientData),
                    attestationObject = B64(stream.ToArray()), transports = new[] { "usb" } },
                clientExtensionResults = new { }
            });
        }

        public string Assert(JsonElement options, bool verified = true, string origin = "https://fullworth.org")
        {
            var client = ClientData("webauthn.get", options, origin);
            var auth = AuthData((byte)(verified ? 5 : 1), 1);
            var signature = _key.SignData(auth.Concat(SHA256.HashData(client)).ToArray(),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            return JsonSerializer.Serialize(new
            {
                id = B64(_id), rawId = B64(_id), type = "public-key",
                response = new { clientDataJSON = B64(client), authenticatorData = B64(auth),
                    signature = B64(signature), userHandle = _userHandle },
                clientExtensionResults = new { }
            });
        }
        private static byte[] ClientData(string type, JsonElement options, string origin) =>
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                type, challenge = options.GetProperty("challenge").GetString(), origin, crossOrigin = false
            });
        private static byte[] AuthData(byte flags, byte count) =>
            SHA256.HashData(Encoding.UTF8.GetBytes("fullworth.org"))
                .Concat(new byte[] { flags, 0, 0, 0, count }).ToArray();
        private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static void Text(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.WriteByte((byte)(0x60 + bytes.Length)); stream.Write(bytes);
        }
        private static void ByteString(Stream stream, byte[] bytes)
        {
            stream.WriteByte(0x59); stream.WriteByte((byte)(bytes.Length >> 8));
            stream.WriteByte((byte)bytes.Length); stream.Write(bytes);
        }
        public void Dispose() => _key.Dispose();
    }
}

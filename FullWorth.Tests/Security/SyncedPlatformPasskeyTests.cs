using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FullWorth.API;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

// Exercises real Identity verification with signed, discoverable platform
// credentials. This is protocol evidence, not Safari or physical Face ID.
public sealed class SyncedPlatformPasskeyTests
{
    private const string Password = "FullWorth!Tests123";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZeroCounterPlatformKey_CanSignInRepeatedly_AndTrackBackupState(bool initiallyBackedUp)
    {
        await using var original = new FullWorthApiFactory();
        await using var factory = Enable(original);
        using var client = Client(factory);
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        using var key = new PlatformKey();
        await Enroll(client, owner, key, initiallyBackedUp);
        client.DefaultRequestHeaders.Authorization = null;

        foreach (var backedUp in new[] { initiallyBackedUp, !initiallyBackedUp, initiallyBackedUp })
        {
            var options = await Begin(client, "/api/auth/passkeys/options", new { });
            Assert.Equal("fullworth.org", options.GetProperty("options").GetProperty("rpId").GetString());
            Assert.Equal("required", options.GetProperty("options").GetProperty("userVerification").GetString());
            Assert.Equal(0, options.GetProperty("options").GetProperty("allowCredentials").GetArrayLength());
            var body = new { ceremonyId = Id(options), credentialJson = key.Assert(options.GetProperty("options"),
                flags: Flags(backedUp)) };
            using var response = await client.PostAsJsonAsync("/api/auth/passkeys/login", body);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            await AssertSession(client, response);
            // A zero counter does not weaken challenge single use.
            using var replay = await client.PostAsJsonAsync("/api/auth/passkeys/login", body);
            Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
            await AssertStored(factory, owner.Email, backedUp);
        }
    }

    [Theory]
    [InlineData("no-presence")]
    [InlineData("no-verification")]
    [InlineData("backup-eligibility-changed")]
    [InlineData("backed-up-without-eligibility")]
    [InlineData("wrong-origin")]
    [InlineData("wrong-rp")]
    [InlineData("invalid-signature")]
    [InlineData("old-challenge")]
    public async Task InvalidPlatformAssertion_DoesNotIssueSessionOrChangeStoredCredential(string failure)
    {
        await using var original = new FullWorthApiFactory();
        await using var factory = Enable(original);
        using var client = Client(factory);
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        using var key = new PlatformKey();
        await Enroll(client, owner, key, backedUp: true);
        client.DefaultRequestHeaders.Authorization = null;
        var options = await Begin(client, "/api/auth/passkeys/options", new { });
        var assertion = key.Assert(options.GetProperty("options"),
            flags: failure switch {
                "no-presence" => 0x1c,
                "no-verification" => 0x19,
                "backup-eligibility-changed" => 0x05,
                "backed-up-without-eligibility" => 0x15,
                _ => 0x1d
            },
            origin: failure == "wrong-origin" ? "https://other.example" : "https://fullworth.org",
            rp: failure == "wrong-rp" ? "other.example" : "fullworth.org",
            invalidSignature: failure == "invalid-signature");
        if (failure == "old-challenge")
            options = await Begin(client, "/api/auth/passkeys/options", new { });
        using var response = await client.PostAsJsonAsync("/api/auth/passkeys/login",
            new { ceremonyId = Id(options), credentialJson = assertion });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("accessToken", content);
        Assert.DoesNotContain("refreshToken", content);
        await AssertStored(factory, owner.Email, backedUp: true);

        // Failed completion consumed its challenge, but did not break the key.
        using var consumed = await client.PostAsJsonAsync("/api/auth/passkeys/login",
            new { ceremonyId = Id(options), credentialJson = key.Assert(options.GetProperty("options")) });
        Assert.Equal(HttpStatusCode.Unauthorized, consumed.StatusCode);
        var fresh = await Begin(client, "/api/auth/passkeys/options", new { });
        using var valid = await client.PostAsJsonAsync("/api/auth/passkeys/login",
            new { ceremonyId = Id(fresh), credentialJson = key.Assert(fresh.GetProperty("options")) });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlatformFirstFactor_PreservesAuthenticatorMfa(bool validCode)
    {
        await using var original = new FullWorthApiFactory();
        await using var factory = Enable(original);
        using var client = Client(factory);
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        using var key = new PlatformKey();
        await Enroll(client, owner, key, backedUp: true);
        var secret = await EnableMfa(factory, owner.Email);
        client.DefaultRequestHeaders.Authorization = null;
        var options = await Begin(client, "/api/auth/passkeys/options", new { });
        using var response = await client.PostAsJsonAsync("/api/auth/passkeys/login", new
        {
            ceremonyId = Id(options), credentialJson = key.Assert(options.GetProperty("options"), flags: Flags(false)),
            twoFactorCode = validCode ? PasswordSignInTests.Code(secret) : "invalid"
        });
        Assert.Equal(validCode ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, response.StatusCode);
        if (validCode) await AssertSession(client, response);
        else Assert.DoesNotContain("accessToken", await response.Content.ReadAsStringAsync());
        await AssertStored(factory, owner.Email, backedUp: !validCode);
    }

    [Fact]
    public async Task SyncedKey_CompletesBoundPasswordMfa_AndCannotReplay()
    {
        await using var original = new FullWorthApiFactory();
        await using var factory = Enable(original);
        using var client = Client(factory);
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        using var key = new PlatformKey();
        await Enroll(client, owner, key, backedUp: true);
        await EnableMfa(factory, owner.Email);
        client.DefaultRequestHeaders.Authorization = null;
        using var start = await client.PostAsJsonAsync("/api/auth/password/start", new { email = owner.Email, password = Password });
        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var pending = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(pending.GetProperty("canUsePasskey").GetBoolean());
        Assert.False(pending.TryGetProperty("accessToken", out _));
        var loginId = pending.GetProperty("loginId").GetString();
        var options = await Begin(client, "/api/auth/password/passkey-options", new { loginId });
        var body = new { loginId, ceremonyId = Id(options), credentialJson = key.Assert(options.GetProperty("options")) };
        using var response = await client.PostAsJsonAsync("/api/auth/password/complete", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSession(client, response);
        using var replay = await client.PostAsJsonAsync("/api/auth/password/complete", body);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    private static byte Flags(bool backedUp) => (byte)(backedUp ? 0x1d : 0x0d);

    private static WebApplicationFactory<ApiAssemblyMarker> Enable(FullWorthApiFactory factory) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?> { ["Passkeys:Enabled"] = "true" })));

    private static HttpClient Client(WebApplicationFactory<ApiAssemblyMarker> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    private static string Id(JsonElement options) => options.GetProperty("ceremonyId").GetString()!;

    private static async Task<JsonElement> Begin(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task Enroll(HttpClient client, TestUserSession owner, PlatformKey key, bool backedUp)
    {
        TestUserAuthentication.Authorize(client, owner);
        var options = await Begin(client, "/api/account/security/passkeys/options", new { currentPassword = Password });
        var creation = options.GetProperty("options");
        Assert.Equal("fullworth.org", creation.GetProperty("rp").GetProperty("id").GetString());
        var selection = creation.GetProperty("authenticatorSelection");
        Assert.Equal("required", selection.GetProperty("residentKey").GetString());
        Assert.Equal("required", selection.GetProperty("userVerification").GetString());
        Assert.True(!selection.TryGetProperty("authenticatorAttachment", out var attachment) ||
            attachment.ValueKind == JsonValueKind.Null);
        using var response = await client.PostAsJsonAsync("/api/account/security/passkeys/register",
            new { ceremonyId = Id(options), credentialJson = key.Register(creation, backedUp), name = "Platform test key" });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await AssertStoredFromClient(client);
    }

    private static async Task AssertStoredFromClient(HttpClient client)
    {
        using var response = await client.GetAsync("/api/account/security/passkeys");
        response.EnsureSuccessStatusCode();
        var keys = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, keys.GetArrayLength());
        Assert.Equal("Platform test key", keys[0].GetProperty("name").GetString());
    }

    private static async Task AssertStored(WebApplicationFactory<ApiAssemblyMarker> factory, string email, bool backedUp)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        var stored = Assert.Single(await users.GetPasskeysAsync(user));
        Assert.Equal(0u, stored.SignCount);
        Assert.True(stored.IsBackupEligible);
        Assert.Equal(backedUp, stored.IsBackedUp);
        Assert.Contains("internal", stored.Transports!);
        Assert.Equal("Platform test key", stored.Name);
    }

    private static async Task<string> EnableMfa(WebApplicationFactory<ApiAssemblyMarker> factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
        Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
        return (await users.GetAuthenticatorKeyAsync(user))!;
    }

    private static async Task AssertSession(HttpClient client, HttpResponseMessage response)
    {
        var tokens = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(string.IsNullOrWhiteSpace(tokens.GetProperty("accessToken").GetString()));
        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh",
            new { refreshToken = tokens.GetProperty("refreshToken").GetString() });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    // No platform/biometric service is mocked. A test ES256 key signs the real
    // challenge, RP hash, UP/UV/BE/BS flags and a permanently zero counter.
    private sealed class PlatformKey : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _id = RandomNumberGenerator.GetBytes(32);
        private string _userHandle = "";

        public string Register(JsonElement options, bool backedUp)
        {
            _userHandle = options.GetProperty("user").GetProperty("id").GetString()!;
            var p = _key.ExportParameters(false);
            var cose = new byte[] { 0xa5, 1, 2, 3, 0x26, 0x20, 1, 0x21, 0x58, 0x20 }
                .Concat(p.Q.X!).Concat(new byte[] { 0x22, 0x58, 0x20 }).Concat(p.Q.Y!).ToArray();
            var auth = AuthData((byte)(Flags(backedUp) | 0x40), "fullworth.org")
                .Concat(new byte[16]).Concat(new byte[] { 0, 32 }).Concat(_id).Concat(cose).ToArray();
            using var stream = new MemoryStream();
            stream.WriteByte(0xa3);
            Text(stream, "fmt"); Text(stream, "none");
            Text(stream, "authData"); ByteString(stream, auth);
            Text(stream, "attStmt"); stream.WriteByte(0xa0);
            return JsonSerializer.Serialize(new {
                id = B64(_id), rawId = B64(_id), type = "public-key", authenticatorAttachment = "platform",
                response = new { clientDataJSON = B64(ClientData("webauthn.create", options, "https://fullworth.org")),
                    attestationObject = B64(stream.ToArray()), transports = new[] { "internal" } },
                clientExtensionResults = new { }
            });
        }

        public string Assert(JsonElement options, byte flags = 0x1d, string origin = "https://fullworth.org",
            string rp = "fullworth.org", bool invalidSignature = false)
        {
            var client = ClientData("webauthn.get", options, origin);
            var auth = AuthData(flags, rp);
            var signature = _key.SignData(auth.Concat(SHA256.HashData(client)).ToArray(),
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
            if (invalidSignature) signature[^1] ^= 1;
            return JsonSerializer.Serialize(new {
                id = B64(_id), rawId = B64(_id), type = "public-key", authenticatorAttachment = "platform",
                response = new { clientDataJSON = B64(client), authenticatorData = B64(auth),
                    signature = B64(signature), userHandle = _userHandle },
                clientExtensionResults = new { }
            });
        }
        private static byte[] AuthData(byte flags, string rp) =>
            SHA256.HashData(Encoding.UTF8.GetBytes(rp)).Concat(new byte[] { flags, 0, 0, 0, 0 }).ToArray();
        private static byte[] ClientData(string type, JsonElement options, string origin) =>
            JsonSerializer.SerializeToUtf8Bytes(new { type, challenge = options.GetProperty("challenge").GetString(), origin, crossOrigin = false });
        private static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        private static void Text(Stream stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            stream.WriteByte((byte)(0x60 + bytes.Length)); stream.Write(bytes);
        }
        private static void ByteString(Stream stream, byte[] bytes)
        {
            stream.WriteByte(0x59); stream.WriteByte((byte)(bytes.Length >> 8)); stream.WriteByte((byte)bytes.Length); stream.Write(bytes);
        }
        public void Dispose() => _key.Dispose();
    }
}

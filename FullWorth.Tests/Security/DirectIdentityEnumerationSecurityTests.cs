using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Legal;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class DirectIdentityEnumerationSecurityTests
{
    private const string Password = "FullWorth!Tests123";

    [Fact]
    public async Task DuplicateRegistration_MatchesNewRegistration_AndPreservesExistingAccount()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var email = $"register-{Guid.NewGuid():N}@fullworth.local";

        using var created = await RegisterAsync(client, email, Password);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var before = await ReadStateAsync(factory, email);

        // Case changes must not bypass Identity's normalized uniqueness check.
        const string replacement = "FullWorth!Replacement456";
        using var duplicate = await RegisterAsync(client, email.ToUpperInvariant(), replacement);
        await AssertSameResponseAsync(created, duplicate);
        Assert.Equal(before, await ReadStateAsync(factory, email));

        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await manager.CheckPasswordAsync(user!, Password));
        Assert.False(await manager.CheckPasswordAsync(user!, replacement));
        Assert.Single(manager.Users.Where(candidate => candidate.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Fact]
    public async Task InvalidPassword_KnownAndUnknownEmail_ReturnSameValidationErrors()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var known = await CreateUserAsync(factory);
        using var existing = await RegisterAsync(client, known.Email!, "short");
        using var unknown = await RegisterAsync(client, $"unknown-{Guid.NewGuid():N}@fullworth.local", "short");
        Assert.Equal(HttpStatusCode.BadRequest, existing.StatusCode);
        Assert.Equal(existing.StatusCode, unknown.StatusCode);
        Assert.Equal(await ReadErrorsAsync(existing), await ReadErrorsAsync(unknown));
    }

    [Theory]
    [InlineData("!", false)]
    [InlineData("bm90LWEtdmFsaWQtdG9rZW4", false)]
    [InlineData("!", true)]
    [InlineData("bm90LWEtdmFsaWQtdG9rZW4", true)]
    public async Task InvalidConfirmationProof_KnownAndUnknownUser_ReturnSameResponse(
        string code,
        bool changeEmail)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var user = await CreateUserAsync(factory);
        var before = await ReadStateAsync(factory, user.Email!);
        var suffix = changeEmail ? "&changedEmail=replacement%40fullworth.local" : string.Empty;

        using var known = await client.GetAsync(
            $"/api/auth/confirmEmail?userId={user.Id}&code={Uri.EscapeDataString(code)}{suffix}");
        using var unknown = await client.GetAsync(
            $"/api/auth/confirmEmail?userId={Guid.NewGuid()}&code={Uri.EscapeDataString(code)}{suffix}");

        Assert.Equal(HttpStatusCode.Unauthorized, known.StatusCode);
        await AssertSameResponseAsync(known, unknown);
        Assert.Equal(before, await ReadStateAsync(factory, user.Email!));
    }

    [Fact]
    public async Task ValidConfirmationProof_StillConfirmsEmail()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var user = await CreateUserAsync(factory);
        string code;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = await manager.FindByIdAsync(user.Id.ToString());
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(
                await manager.GenerateEmailConfirmationTokenAsync(stored!)));
        }

        using var response = await client.GetAsync($"/api/auth/confirmEmail?userId={user.Id}&code={code}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReadStateAsync(factory, user.Email!)).EmailConfirmed);
    }

    private static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            acceptedTermsAndPrivacy = true,
            legalTermsVersion = FullWorthLegalDocuments.CurrentVersion
        });

    private static async Task<ApplicationUser> CreateUserAsync(FullWorthApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"identity-{Guid.NewGuid():N}@fullworth.local";
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email };
        Assert.True((await manager.CreateAsync(user, Password)).Succeeded);
        return user;
    }

    private static async Task<AccountState> ReadStateAsync(FullWorthApiFactory factory, string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.NotNull(user);
        return new AccountState(user!.Id, user.Email, user.UserName, user.EmailConfirmed, user.SecurityStamp);
    }

    private static async Task AssertSameResponseAsync(HttpResponseMessage known, HttpResponseMessage unknown)
    {
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Content.Headers.ContentType?.ToString(), unknown.Content.Headers.ContentType?.ToString());
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        Assert.False(known.Headers.Contains("Set-Cookie"));
        Assert.False(unknown.Headers.Contains("Set-Cookie"));
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    private static async Task<string> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return JsonSerializer.Serialize(document.RootElement.GetProperty("errors").EnumerateObject()
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .Select(property => new
            {
                property.Name,
                Values = property.Value.EnumerateArray().Select(value => value.GetString()).ToArray()
            }).ToArray());
    }

    private sealed record AccountState(Guid Id, string? Email, string? UserName, bool EmailConfirmed, string? SecurityStamp);
}

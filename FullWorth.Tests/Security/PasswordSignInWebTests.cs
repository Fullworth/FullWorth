using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class PasswordSignInWebTests
{
    [Fact]
    public async Task PasswordIsEnteredOnceThenOnlyCodeIsSubmittedAndRememberMeSurvives()
    {
        await using var api = new FullWorthApiFactory();
        using var apiClient = api.CreateHttpsClient();
        var account = await TestUserAuthentication.RegisterAndLoginAsync(apiClient);
        string key;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByEmailAsync(account.Email))!;
            Assert.True((await users.ResetAuthenticatorKeyAsync(user)).Succeeded);
            Assert.True((await users.SetTwoFactorEnabledAsync(user, true)).Succeeded);
            key = (await users.GetAuthenticatorKeyAsync(user))!;
        }
        await using var root = new FullWorthWebFactory();
        await using var web = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient("FullWorthApi").ConfigurePrimaryHttpMessageHandler(() => api.Server.CreateHandler())));
        using var client = web.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
        });
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        var html = await client.GetStringAsync("/login");
        var first = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["email"] = account.Email, ["password"] = "FullWorth!Tests123",
            ["rememberMe"] = "on", ["__RequestVerificationToken"] = Token(html)
        }));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal("/login?twoFactor=true", first.Headers.Location!.OriginalString);
        var pendingCookie = first.Headers.GetValues("Set-Cookie")
            .Last(value => value.StartsWith(PendingPasswordLoginCookie.Name + "="));
        Assert.DoesNotContain("FullWorth!Tests123", pendingCookie);
        var mainCookie = web.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(CookieAuthenticationDefaults.AuthenticationScheme).Cookie.Name!;
        Assert.DoesNotContain(first.Headers.GetValues("Set-Cookie"), value => value.StartsWith(mainCookie + "="));
        html = await client.GetStringAsync(first.Headers.Location);
        Assert.DoesNotContain("name=\"password\"", html);
        Assert.DoesNotContain("name=\"email\"", html);
        Assert.DoesNotContain("Sign in again with your password", html);
        Assert.Contains("name=\"twoFactorCode\"", html);
        Assert.Contains("name=\"recoveryCode\"", html);
        var completed = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["twoFactor"] = "true", ["twoFactorCode"] = PasswordSignInTests.Code(key),
            ["__RequestVerificationToken"] = Token(html)
        }));
        Assert.Equal("/app", completed.Headers.Location!.OriginalString);
        var signedIn = completed.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(mainCookie + "="));
        Assert.Contains("expires=", signedIn, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/auth/login/passkey-options")]
    [InlineData("/auth/login/passkey-complete")]
    public async Task PasskeyContinuationRequiresAntiforgeryAndCannotTrustClientProofIds(string path)
    {
        await using var web = new FullWorthWebFactory();
        using var client = web.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        var response = await client.PostAsJsonAsync(path,
            new { loginId = new string('x', 43), ceremonyId = new string('y', 43), credentialJson = "{}" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SecondStepWithoutProtectedCookieCannotSignIn()
    {
        await using var web = new FullWorthWebFactory();
        using var client = web.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        var html = await client.GetStringAsync("/login?twoFactor=true");
        var response = await client.PostAsync("/auth/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["twoFactor"] = "true", ["twoFactorCode"] = "123456", ["__RequestVerificationToken"] = Token(html)
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("expired", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    private static string Token(string html)
    {
        var input = Regex.Match(html, "<input[^>]*name=\"__RequestVerificationToken\"[^>]*>");
        Assert.True(input.Success);
        var value = Regex.Match(input.Value, "value=\"([^\"]+)\"");
        Assert.True(value.Success);
        return WebUtility.HtmlDecode(value.Groups[1].Value);
    }
}

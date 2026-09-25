using System.Security.Claims;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace FullWorth.Tests.Security;

public sealed class WebSessionFixationBoundaryTests
{
    [Fact]
    public async Task PasswordLogin_RejectsAlreadyAuthenticatedWebSession()
    {
        var service =
            new WebAuthenticationService(
                new ThrowingHttpClientFactory());

        var result =
            await service.LoginAsync(
                CreateAuthenticatedContext(),
                "other@example.com",
                "unused-password",
                rememberMe: false);

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            "Sign out before signing in to another account.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task PasswordRegistration_RejectsAlreadyAuthenticatedWebSession()
    {
        var service =
            new WebAuthenticationService(
                new ThrowingHttpClientFactory());

        var result =
            await service.RegisterAsync(
                CreateAuthenticatedContext(),
                "other@example.com",
                "unused-password",
                acceptedTermsAndPrivacy: true,
                legalTermsVersion: "test");

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            "Sign out before creating another account.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ExternalLogin_RejectsAlreadyAuthenticatedWebSession()
    {
        var result =
            await ExternalWebSignInFlow.LoginAsync(
                CreateAuthenticatedContext(),
                new ThrowingHttpClientFactory(),
                "google",
                "unused-id-token",
                "provider-subject",
                "other@example.com",
                twoFactorCode: null,
                recoveryCode: null);

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            "Sign out before signing in to another account.",
            result.ErrorMessage);
    }

    [Fact]
    public async Task ExternalRegistration_RejectsAlreadyAuthenticatedWebSession()
    {
        var result =
            await ExternalWebSignInFlow.RegisterAsync(
                CreateAuthenticatedContext(),
                new ThrowingHttpClientFactory(),
                "google",
                "unused-id-token",
                "provider-subject",
                "other@example.com",
                "unused-password",
                acceptedTermsAndPrivacy: true,
                legalTermsVersion: "test");

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            "Sign out before creating another account.",
            result.ErrorMessage);
    }

    private static DefaultHttpContext
        CreateAuthenticatedContext()
    {
        var identity =
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "existing-user")
                ],
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        return new DefaultHttpContext
        {
            User =
                new ClaimsPrincipal(
                    identity)
        };
    }

    private sealed class ThrowingHttpClientFactory
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name)
        {
            throw new InvalidOperationException(
                $"HTTP client '{name}' must not be created when an authenticated Web session is already present.");
        }
    }
}

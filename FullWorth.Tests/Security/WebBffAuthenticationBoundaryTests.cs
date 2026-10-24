using System.Net;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class WebBffAuthenticationBoundaryTests
{
    [Theory]
    [InlineData("/bff/antiforgery")]
    [InlineData("/bff/subscription")]
    [InlineData("/bff/subscription/plans")]
    [InlineData("/bff/bill-streams")]
    [InlineData("/bff/bank-accounts")]
    [InlineData("/bff/bank-connections")]
    [InlineData("/bff/bank-transactions")]
    [InlineData("/bff/alerts")]
    public async Task BffReads_RequireAuthenticatedSession(string route)
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");

        using var response = await client.GetAsync(route);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PasskeyContinuation_RequiresAuthenticatedSession()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");

        using var response = await client.GetAsync(
            "/auth/passkeys/continue?returnUrl=%2Fapp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("/app", "/app")]
    [InlineData("/app/bills?tab=history", "/app/bills?tab=history")]
    [InlineData("//outside.example", "/app")]
    [InlineData("/%2f%2foutside.example", "/app")]
    [InlineData("/%252f%252foutside.example", "/app")]
    [InlineData("/%5coutside.example", "/app")]
    [InlineData("/%0d%0aLocation:outside", "/app")]
    [InlineData("/incomplete%", "/app")]
    [InlineData("/auth/passkeys/continue?returnUrl=%2Fapp", "/app")]
    public async Task PasskeyContinuation_AllowsOnlyAuthenticatedLocalRedirects(
        string untrustedDestination, string expectedDestination)
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync(
            "/auth/passkeys/continue?returnUrl=" +
            Uri.EscapeDataString(untrustedDestination));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expectedDestination, response.Headers.Location?.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore,
            "The server must not cache an authenticated completion redirect.");
    }

    [Fact]
    public async Task AccountExport_RequiresAuthenticatedSession()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Test-Anonymous",
            "true");

        using var response =
            await client.PostAsync(
                "/bff/account/export",
                content: null);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task MonitoringRefresh_RequiresAuthenticatedSession()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");

        using var response =
            await client.PostAsync(
                "/bff/bill-monitoring/refresh",
                content: null);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task CookieChallenge_BffRequest_ReturnsUnauthorizedWithoutRedirect()
    {
        using var factory =
            new FullWorthWebFactory();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        CookieAuthenticationOptions>>()
                .Get(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

        var httpContext =
            new DefaultHttpContext();

        httpContext.Request.Path =
            "/bff/bill-streams";

        var redirectContext =
            CreateRedirectContext(
                httpContext,
                options,
                "/login?ReturnUrl=%2Fbff%2Fbill-streams");

        await options.Events
            .OnRedirectToLogin(
                redirectContext);

        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            httpContext.Response.StatusCode);

        Assert.False(
            httpContext.Response.Headers
                .ContainsKey("Location"));
    }

    [Fact]
    public async Task CookieChallenge_NormalPage_KeepsLoginRedirect()
    {
        using var factory =
            new FullWorthWebFactory();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        CookieAuthenticationOptions>>()
                .Get(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

        var httpContext =
            new DefaultHttpContext();

        httpContext.Request.Path =
            "/app";

        const string redirectUri =
            "/login?ReturnUrl=%2Fapp";

        var redirectContext =
            CreateRedirectContext(
                httpContext,
                options,
                redirectUri);

        await options.Events
            .OnRedirectToLogin(
                redirectContext);

        Assert.Equal(
            StatusCodes.Status302Found,
            httpContext.Response.StatusCode);

        Assert.Equal(
            redirectUri,
            httpContext.Response.Headers
                .Location.ToString());
    }

    [Fact]
    public async Task CookieAccessDenied_BffRequest_ReturnsForbiddenWithoutRedirect()
    {
        using var factory =
            new FullWorthWebFactory();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        CookieAuthenticationOptions>>()
                .Get(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

        var httpContext =
            new DefaultHttpContext();

        httpContext.Request.Path =
            "/bff/admin/users";

        var redirectContext =
            CreateRedirectContext(
                httpContext,
                options,
                "/login?ReturnUrl=%2Fbff%2Fadmin%2Fusers");

        await options.Events
            .OnRedirectToAccessDenied(
                redirectContext);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            httpContext.Response.StatusCode);

        Assert.False(
            httpContext.Response.Headers
                .ContainsKey("Location"));
    }

    private static RedirectContext<
        CookieAuthenticationOptions>
        CreateRedirectContext(
            HttpContext httpContext,
            CookieAuthenticationOptions options,
            string redirectUri)
    {
        return new RedirectContext<
            CookieAuthenticationOptions>(
                httpContext,
                new AuthenticationScheme(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme,
                    displayName: null,
                    typeof(
                        CookieAuthenticationHandler)),
                options,
                new AuthenticationProperties(),
                redirectUri);
    }
}

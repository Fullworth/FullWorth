using FullWorth.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace FullWorth.Tests.Security;

public sealed class WebOidcSecurityConfigurationTests
{
    private const string GoogleScheme =
        "FullWorth.Web.Google";

    [Fact]
    public void GoogleOidc_UsesCodePkceAndStrictTokenValidation()
    {
        using var services =
            BuildServices();

        var options =
            services
                .GetRequiredService<
                    IOptionsMonitor<
                        OpenIdConnectOptions>>()
                .Get(
                    GoogleScheme);

        Assert.Equal(
            OpenIdConnectResponseType.Code,
            options.ResponseType);

        Assert.Equal(
            OpenIdConnectResponseMode.Query,
            options.ResponseMode);

        Assert.True(
            options.UsePkce);

        Assert.True(
            options.RequireHttpsMetadata);

        Assert.False(
            options.SaveTokens);

        Assert.False(
            options.GetClaimsFromUserInfoEndpoint);

        Assert.False(
            options.MapInboundClaims);

        Assert.True(
            options.TokenValidationParameters
                .ValidateIssuer);

        Assert.True(
            options.TokenValidationParameters
                .ValidateAudience);

        Assert.Contains(
            OpenIdConnectScope.OpenId,
            options.Scope);

        Assert.Contains(
            OpenIdConnectScope.Email,
            options.Scope);

        Assert.Contains(
            OpenIdConnectScope.Profile,
            options.Scope);

        Assert.DoesNotContain(
            OpenIdConnectScope.OfflineAccess,
            options.Scope);
    }

    [Fact]
    public void GoogleOidc_NonceAndCorrelationCookiesAreHostOnlySecureAndCrossSiteCompatible()
    {
        using var services =
            BuildServices();

        var options =
            services
                .GetRequiredService<
                    IOptionsMonitor<
                        OpenIdConnectOptions>>()
                .Get(
                    GoogleScheme);

        AssertRemoteCookie(
            options.CorrelationCookie,
            "__Host-BillWatch.Web.Google.Correlation.");

        AssertRemoteCookie(
            options.NonceCookie,
            "__Host-BillWatch.Web.Google.Nonce.");
    }

    [Fact]
    public void ExternalProofCookie_IsShortLivedSecureAndNonSliding()
    {
        using var services =
            BuildServices();

        var options =
            services
                .GetRequiredService<
                    IOptionsMonitor<
                        CookieAuthenticationOptions>>()
                .Get(
                    ExternalAuthenticationEndpointMappings
                        .ExternalCookieScheme);

        Assert.Equal(
            "__Host-BillWatch.Web.External",
            options.Cookie.Name);

        Assert.True(
            options.Cookie.HttpOnly);

        Assert.Equal(
            CookieSecurePolicy.Always,
            options.Cookie.SecurePolicy);

        Assert.Equal(
            SameSiteMode.Lax,
            options.Cookie.SameSite);

        Assert.Equal(
            "/",
            options.Cookie.Path);

        Assert.True(
            string.IsNullOrWhiteSpace(
                options.Cookie.Domain));

        Assert.Equal(
            TimeSpan.FromMinutes(5),
            options.ExpireTimeSpan);

        Assert.False(
            options.SlidingExpiration);
    }

    private static ServiceProvider
        BuildServices()
    {
        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<
                        string,
                        string?>
                    {
                        [
                            "ExternalIdentity:Google:ClientId"
                        ] =
                            "oidc-security-test-client",

                        [
                            "ExternalIdentity:Google:ClientSecret"
                        ] =
                            "oidc-security-test-secret"
                    })
                .Build();

        var services =
            new ServiceCollection();

        services.AddLogging();
        services.AddOptions();
        services.AddDataProtection();

        var authentication =
            services.AddAuthentication(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme)
                .AddCookie();

        authentication
            .AddFullWorthExternalAuthentication(
                configuration);

        return services
            .BuildServiceProvider();
    }

    private static void AssertRemoteCookie(
        CookieBuilder cookie,
        string expectedName)
    {
        Assert.Equal(
            expectedName,
            cookie.Name);

        Assert.True(
            cookie.HttpOnly);

        Assert.Equal(
            CookieSecurePolicy.Always,
            cookie.SecurePolicy);

        Assert.Equal(
            SameSiteMode.None,
            cookie.SameSite);

        Assert.Equal(
            "/",
            cookie.Path);

        Assert.True(
            string.IsNullOrWhiteSpace(
                cookie.Domain));

        Assert.True(
            cookie.IsEssential);
    }
}

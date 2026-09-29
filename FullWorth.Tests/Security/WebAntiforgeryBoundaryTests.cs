using System.Net;
using System.Net.Http.Json;
using System.Text;
using FullWorth.Tests.Infrastructure;
using FullWorth.Web.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class WebAntiforgeryBoundaryTests
{
    public static IEnumerable<object[]> UnsafeWebEndpoints()
    {
        var id = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        yield return ["POST", "/auth/login"];
        yield return ["POST", "/auth/register"];
        yield return ["POST", "/auth/forgot-password"];
        yield return ["POST", "/auth/reset-password"];
        yield return ["POST", "/auth/logout"];

        yield return ["POST", "/bff/subscription/checkout"];
        yield return ["POST", "/bff/subscription/billing-portal"];
        yield return ["POST", "/bff/subscription/sync"];
        yield return ["POST", "/bff/subscription/access-keys/redeem"];

        yield return ["POST", $"/bff/alerts/{id:D}/read"];
        yield return ["POST", $"/bff/alerts/{id:D}/dismiss"];
        yield return ["POST", "/bff/account/export"];
        yield return ["DELETE", "/bff/account"];
        yield return ["POST", "/bff/plaid/link-session"];
        yield return ["POST", $"/bff/plaid/connections/{id:D}/update-link-session"];
        yield return ["POST", $"/bff/plaid/link-session/{id:D}/complete"];
        yield return ["DELETE", $"/bff/bank-connections/{id:D}"];
        yield return ["POST", $"/bff/bill-streams/{id:D}/statement-uploads"];

        yield return ["PUT", "/bff/account/preferences"];
        yield return ["POST", "/bff/account/security/profile"];
        yield return ["POST", "/bff/account/security/password"];
        yield return ["POST", "/bff/account/security/sessions/revoke-all"];
        yield return ["POST", "/bff/account/security/email"];
        yield return ["POST", "/bff/account/security/two-factor/setup"];
        yield return ["POST", "/bff/account/security/two-factor/enable"];
        yield return ["POST", "/bff/account/security/two-factor/recovery-codes"];
        yield return ["POST", "/bff/account/security/two-factor/disable"];
        yield return ["POST", "/bff/account/security/two-factor/reset"];
        yield return ["POST", "/bff/account/external/link"];

        yield return ["POST", $"/bff/admin/users/{id:D}/roles/Admin"];
        yield return ["DELETE", $"/bff/admin/users/{id:D}/roles/Moderator"];
        yield return ["POST", $"/bff/admin/users/{id:D}/entitlements"];
        yield return ["POST", $"/bff/admin/users/{id:D}/entitlements/{secondId:D}/revoke"];
        yield return ["PUT", $"/bff/admin/users/{id:D}/programs/BetaTester"];
        yield return ["POST", "/bff/admin/access-keys"];
        yield return ["POST", $"/bff/admin/access-keys/{id:D}/revoke"];
    }

    [Theory]
    [MemberData(nameof(UnsafeWebEndpoints))]
    public async Task UnsafeWebEndpoints_RejectMissingAntiforgeryToken(
        string method,
        string route)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                new HttpMethod(method),
                route);

        if (!HttpMethods.IsDelete(method))
        {
            request.Content =
                new StringContent(
                    "{}",
                    Encoding.UTF8,
                    "application/json");
        }

        using var response =
            await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task AntiforgeryEndpoint_IssuesTokenAndNoStoreHeaders()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/bff/antiforgery");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var payload =
            await response.Content.ReadFromJsonAsync<AntiforgeryPayload>();

        Assert.NotNull(payload);
        Assert.False(
            string.IsNullOrWhiteSpace(
                payload!.RequestToken));

        Assert.True(
            response.Headers.TryGetValues(
                "Cache-Control",
                out var cacheControl));

        Assert.Contains(
            cacheControl,
            value =>
                value.Contains(
                    "no-store",
                    StringComparison.OrdinalIgnoreCase));

        AssertSecurityHeaders(response);
    }

    [Fact]
    public async Task WebResponses_UseServerGeneratedRequestIdInsteadOfClientValue()
    {
        const string spoofedRequestId =
            "attacker-controlled-request-id";

        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Request-Id",
            spoofedRequestId);

        using var firstResponse =
            await client.GetAsync(
                "/bff/antiforgery");

        using var secondResponse =
            await client.GetAsync(
                "/bff/antiforgery");

        var firstRequestId =
            Assert.Single(
                firstResponse.Headers.GetValues(
                    "X-FullWorth-Request-Id"));

        var secondRequestId =
            Assert.Single(
                secondResponse.Headers.GetValues(
                    "X-FullWorth-Request-Id"));

        Assert.Matches(
            "^[0-9a-f]{32}$",
            firstRequestId);

        Assert.Matches(
            "^[0-9a-f]{32}$",
            secondRequestId);

        Assert.NotEqual(
            spoofedRequestId,
            firstRequestId);

        Assert.NotEqual(
            firstRequestId,
            secondRequestId);
    }

    [Fact]
    public void AntiforgeryCookie_IsHostOnlySecureAndStrict()
    {
        using var factory =
            new FullWorthWebFactory();

        var antiforgeryOptions =
            factory.Services
                .GetRequiredService<
                    IOptions<
                        AntiforgeryOptions>>()
                .Value;

        Assert.Equal(
            "__Host-BillWatch.Web.Antiforgery",
            antiforgeryOptions.Cookie.Name);

        Assert.True(
            antiforgeryOptions.Cookie.HttpOnly);

        Assert.Equal(
            CookieSecurePolicy.Always,
            antiforgeryOptions.Cookie.SecurePolicy);

        Assert.Equal(
            SameSiteMode.Strict,
            antiforgeryOptions.Cookie.SameSite);

        Assert.Equal(
            "/",
            antiforgeryOptions.Cookie.Path);

        Assert.True(
            string.IsNullOrWhiteSpace(
                antiforgeryOptions.Cookie.Domain));

        Assert.True(
            antiforgeryOptions.Cookie.IsEssential);
    }

    [Fact]
    public void AuthenticationCookie_IsHostOnlySecureAndNonSliding()
    {
        using var factory =
            new FullWorthWebFactory();

        var cookieOptions =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        CookieAuthenticationOptions>>()
                .Get(
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

        Assert.Equal(
            "__Host-BillWatch.Web.Auth",
            cookieOptions.Cookie.Name);

        Assert.True(
            cookieOptions.Cookie.HttpOnly);

        Assert.Equal(
            CookieSecurePolicy.Always,
            cookieOptions.Cookie.SecurePolicy);

        Assert.Equal(
            SameSiteMode.Lax,
            cookieOptions.Cookie.SameSite);

        Assert.Equal(
            "/",
            cookieOptions.Cookie.Path);

        Assert.True(
            string.IsNullOrWhiteSpace(
                cookieOptions.Cookie.Domain));

        Assert.False(
            cookieOptions.SlidingExpiration);

        Assert.IsType<
            ProtectedDistributedTicketStore>(
                cookieOptions.SessionStore);
    }

    [Fact]
    public async Task SafeAuthGet_DoesNotRequireAntiforgeryToken()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/auth/confirm-email");

        Assert.Equal(
            HttpStatusCode.Redirect,
            response.StatusCode);
    }

    [Fact]
    public async Task UnknownUnsafeBffRoute_IsNotRejectedAsAntiforgeryFailure()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsync(
                "/bff/not-a-real-route",
                new StringContent(
                    "{}",
                    Encoding.UTF8,
                    "application/json"));

        Assert.Equal(
            HttpStatusCode.MethodNotAllowed,
            response.StatusCode);

        Assert.NotEqual(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task WebDocument_UsesFreshCspNonceForImportMap()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var firstResponse =
            await client.GetAsync("/");

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

        var firstPolicy =
            Assert.Single(
                firstResponse.Headers.GetValues(
                    "Content-Security-Policy"),
                value =>
                    value.Contains(
                        "script-src 'self' 'nonce-",
                        StringComparison.Ordinal));

        Assert.Contains(
            "default-src 'self'",
            firstPolicy,
            StringComparison.Ordinal);

        Assert.Contains(
            "style-src 'self'",
            firstPolicy,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "'unsafe-inline'",
            firstPolicy,
            StringComparison.Ordinal);

        Assert.Contains(
            "script-src 'self' 'nonce-",
            firstPolicy,
            StringComparison.Ordinal);

        var firstNonce =
            ExtractCspNonce(firstPolicy);

        var firstHtml =
            await firstResponse.Content.ReadAsStringAsync();

        AssertImportMapHasNonce(
            firstHtml,
            firstNonce);

        using var secondResponse =
            await client.GetAsync("/");

        var secondPolicy =
            Assert.Single(
                secondResponse.Headers.GetValues(
                    "Content-Security-Policy"),
                value =>
                    value.Contains(
                        "script-src 'self' 'nonce-",
                        StringComparison.Ordinal));

        Assert.NotEqual(
            firstNonce,
            ExtractCspNonce(secondPolicy));
    }

    private static string ExtractCspNonce(
        string policy)
    {
        const string noncePrefix =
            "'nonce-";

        var nonceStart =
            policy.IndexOf(
                noncePrefix,
                StringComparison.Ordinal);

        Assert.True(nonceStart >= 0);

        var valueStart =
            nonceStart + noncePrefix.Length;

        var valueEnd =
            policy.IndexOf(
                "';",
                valueStart,
                StringComparison.Ordinal);

        Assert.True(valueEnd > valueStart);

        return policy[valueStart..valueEnd];
    }

    private static void AssertImportMapHasNonce(
        string html,
        string nonce)
    {
        var importMapIndex =
            html.IndexOf(
                "importmap",
                StringComparison.OrdinalIgnoreCase);

        Assert.True(importMapIndex >= 0);

        var tagStart =
            html.LastIndexOf(
                "<script",
                importMapIndex,
                StringComparison.OrdinalIgnoreCase);

        Assert.True(tagStart >= 0);

        var tagEnd =
            html.IndexOf(
                '>',
                importMapIndex);

        Assert.True(tagEnd > importMapIndex);

        var importMapTag =
            html[tagStart..tagEnd];

        Assert.Contains(
            "nonce=",
            importMapTag,
            StringComparison.Ordinal);

        Assert.Contains(
            nonce,
            WebUtility.HtmlDecode(
                importMapTag),
            StringComparison.Ordinal);
    }

    private static void AssertSecurityHeaders(
        HttpResponseMessage response)
    {
        AssertHeader(
            response,
            "X-Content-Type-Options",
            "nosniff");

        AssertHeader(
            response,
            "X-Frame-Options",
            "DENY");

        AssertHeader(
            response,
            "Referrer-Policy",
            "no-referrer");

        Assert.True(
            response.Headers.TryGetValues(
                "Content-Security-Policy",
                out var contentSecurityPolicy));

        Assert.Contains(
            contentSecurityPolicy,
            value =>
                value.Contains(
                    "frame-ancestors 'none'",
                    StringComparison.Ordinal));
    }

    private static void AssertHeader(
        HttpResponseMessage response,
        string name,
        string expectedValue)
    {
        Assert.True(
            response.Headers.TryGetValues(
                name,
                out var values));

        Assert.Contains(
            expectedValue,
            values);
    }

    private sealed record AntiforgeryPayload(
        string RequestToken);
}

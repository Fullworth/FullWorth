using System.Net;
using System.Security.Claims;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class AdminBffWriteProxySessionSecurityTests
{
    [Fact]
    public async Task PasswordChange_Success_SignsOutWebSession()
    {
        var authentication =
            new RecordingAuthenticationService();

        var httpContext =
            CreateHttpContext(authentication);

        var proxy =
            CreateProxy(
                HttpStatusCode.NoContent);

        _ =
            await proxy.ForwardJsonAsync(
                httpContext,
                HttpMethod.Post,
                "/api/account/security/password",
                new
                {
                    currentPassword =
                        "current-password",

                    newPassword =
                        "replacement-password",

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            1,
            authentication.SignOutCount);

        Assert.Equal(
            CookieAuthenticationDefaults
                .AuthenticationScheme,
            authentication.LastSignOutScheme);
    }

    [Fact]
    public async Task PasswordChange_Failure_PreservesWebSession()
    {
        var authentication =
            new RecordingAuthenticationService();

        var httpContext =
            CreateHttpContext(authentication);

        var proxy =
            CreateProxy(
                HttpStatusCode.BadRequest);

        _ =
            await proxy.ForwardJsonAsync(
                httpContext,
                HttpMethod.Post,
                "/api/account/security/password",
                new
                {
                    currentPassword =
                        "wrong-password",

                    newPassword =
                        "replacement-password",

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            0,
            authentication.SignOutCount);
    }

    [Fact]
    public async Task OrdinaryAccountSecurityWrite_DoesNotSignOutWebSession()
    {
        var authentication =
            new RecordingAuthenticationService();

        var httpContext =
            CreateHttpContext(authentication);

        var proxy =
            CreateProxy(
                HttpStatusCode.NoContent);

        _ =
            await proxy.ForwardJsonAsync(
                httpContext,
                HttpMethod.Post,
                "/api/account/security/profile",
                new
                {
                    displayName =
                        "Security Test"
                });

        Assert.Equal(
            0,
            authentication.SignOutCount);
    }

    private static DefaultHttpContext
        CreateHttpContext(
            RecordingAuthenticationService
                authentication)
    {
        var services =
            new ServiceCollection()
                .AddSingleton<
                    IAuthenticationService>(
                    authentication)
                .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices =
                services
        };
    }

    private static AdminBffWriteProxyService
        CreateProxy(
            HttpStatusCode responseStatusCode)
    {
        var client =
            new HttpClient(
                new StaticResponseHandler(
                    responseStatusCode))
            {
                BaseAddress =
                    new Uri(
                        "https://api.fullworth.test")
            };

        return new AdminBffWriteProxyService(
            new SingleHttpClientFactory(
                client));
    }

    private sealed class RecordingAuthenticationService
        : IAuthenticationService
    {
        public int SignOutCount { get; private set; }

        public string? LastSignOutScheme { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme)
        {
            var identity =
                new ClaimsIdentity(
                    [
                        new Claim(
                            ClaimTypes.NameIdentifier,
                            Guid.NewGuid()
                                .ToString("D"))
                    ],
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

            var properties =
                new AuthenticationProperties
                {
                    ExpiresUtc =
                        DateTimeOffset.UtcNow
                            .AddMinutes(10)
                };

            properties.StoreTokens(
                [
                    new AuthenticationToken
                    {
                        Name =
                            "access_token",

                        Value =
                            "test-access-token"
                    },

                    new AuthenticationToken
                    {
                        Name =
                            "refresh_token",

                        Value =
                            "test-refresh-token"
                    },

                    new AuthenticationToken
                    {
                        Name =
                            "expires_at",

                        Value =
                            DateTimeOffset.UtcNow
                                .AddMinutes(10)
                                .ToString("O")
                    },

                    new AuthenticationToken
                    {
                        Name =
                            "token_type",

                        Value =
                            "Bearer"
                    }
                ]);

            return Task.FromResult(
                AuthenticateResult.Success(
                    new AuthenticationTicket(
                        new ClaimsPrincipal(
                            identity),
                        properties,
                        CookieAuthenticationDefaults
                            .AuthenticationScheme)));
        }

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            return Task.CompletedTask;
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            SignOutCount++;
            LastSignOutScheme =
                scheme;

            return Task.CompletedTask;
        }
    }

    private sealed class SingleHttpClientFactory(
        HttpClient client)
        : IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name)
        {
            return client;
        }
    }

    private sealed class StaticResponseHandler(
        HttpStatusCode responseStatusCode)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            return Task.FromResult(
                new HttpResponseMessage(
                    responseStatusCode));
        }
    }
}

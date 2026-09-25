using System.Net;
using System.Security.Claims;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class WebPasswordChangeSessionSecurityTests
{
    [Fact]
    public async Task SuccessfulPasswordChangeWrite_SignsOutWebSession()
    {
        using var handler =
            new ScriptedHandler(
                new HttpResponseMessage(
                    HttpStatusCode.NoContent));

        using var clientFactory =
            new SingleClientFactory(
                handler);

        var authentication =
            CreateAuthentication();

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new AdminBffWriteProxyService(
                clientFactory);

        await service.ForwardJsonAndSignOutOnSuccessAsync(
            context,
            HttpMethod.Post,
            "/api/account/security/password",
            new
            {
                value =
                    "test"
            });

        Assert.True(
            authentication.SignedOut);

        var request =
            Assert.Single(
                handler.Requests);

        Assert.Equal(
            "/api/account/security/password",
            request.Path);

        Assert.Equal(
            "Bearer test-access",
            request.Authorization);
    }

    [Fact]
    public async Task RejectedPasswordChangeWrite_KeepsWebSession()
    {
        using var handler =
            new ScriptedHandler(
                new HttpResponseMessage(
                    HttpStatusCode.BadRequest));

        using var clientFactory =
            new SingleClientFactory(
                handler);

        var authentication =
            CreateAuthentication();

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new AdminBffWriteProxyService(
                clientFactory);

        await service.ForwardJsonAndSignOutOnSuccessAsync(
            context,
            HttpMethod.Post,
            "/api/account/security/password",
            new
            {
                value =
                    "test"
            });

        Assert.False(
            authentication.SignedOut);
    }

    private static CapturingAuthenticationService
        CreateAuthentication()
    {
        var identity =
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "password-session-test")
                ],
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var properties =
            new AuthenticationProperties();

        properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name =
                        "access_token",

                    Value =
                        "test-access"
                },

                new AuthenticationToken
                {
                    Name =
                        "refresh_token",

                    Value =
                        "test-refresh"
                },

                new AuthenticationToken
                {
                    Name =
                        "expires_at",

                    Value =
                        DateTimeOffset.UtcNow
                            .AddMinutes(10)
                            .ToString("O")
                }
            ]);

        return new CapturingAuthenticationService(
            AuthenticateResult.Success(
                new AuthenticationTicket(
                    new ClaimsPrincipal(
                        identity),
                    properties,
                    CookieAuthenticationDefaults
                        .AuthenticationScheme)));
    }

    private static DefaultHttpContext
        CreateHttpContext(
            IAuthenticationService authentication)
    {
        var services =
            new ServiceCollection()
                .AddLogging()
                .AddSingleton(
                    authentication)
                .BuildServiceProvider();

        return new DefaultHttpContext
        {
            RequestServices =
                services
        };
    }

    private sealed class SingleClientFactory(
        HttpMessageHandler handler)
        : IHttpClientFactory,
          IDisposable
    {
        private readonly HttpClient _client =
            new(
                handler,
                disposeHandler:
                    false)
            {
                BaseAddress =
                    new Uri(
                        "https://api.invalid")
            };

        public HttpClient CreateClient(
            string name)
        {
            Assert.Equal(
                "FullWorthApi",
                name);

            return _client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    private sealed class ScriptedHandler(
        params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage>
            _responses =
                new(
                    responses);

        public List<CapturedRequest> Requests { get; } =
            [];

        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scripted response remains.");
            }

            Requests.Add(
                new CapturedRequest(
                    request.RequestUri?.AbsolutePath
                    ?? throw new InvalidOperationException(
                        "Request URI was missing."),
                    request.Headers.Authorization?
                        .ToString()));

            return Task.FromResult(
                _responses.Dequeue());
        }
    }

    private sealed record CapturedRequest(
        string Path,
        string? Authorization);

    private sealed class CapturingAuthenticationService(
        AuthenticateResult authenticateResult)
        : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public Task<AuthenticateResult>
            AuthenticateAsync(
                HttpContext context,
                string? scheme)
        {
            return Task.FromResult(
                authenticateResult);
        }

        public Task ChallengeAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task ForbidAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task SignInAsync(
            HttpContext context,
            string? scheme,
            ClaimsPrincipal principal,
            AuthenticationProperties? properties)
        {
            throw new NotSupportedException();
        }

        public Task SignOutAsync(
            HttpContext context,
            string? scheme,
            AuthenticationProperties? properties)
        {
            Assert.Equal(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                scheme);

            SignedOut =
                true;

            return Task.CompletedTask;
        }
    }
}

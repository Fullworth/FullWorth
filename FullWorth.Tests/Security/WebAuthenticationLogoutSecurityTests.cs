using System.Net;
using System.Security.Claims;
using System.Text;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class WebAuthenticationLogoutSecurityTests
{
    [Fact]
    public async Task Login_EnrollsRefreshFamilyBeforeStoringWebSession()
    {
        const string initialAccess =
            "initial-access";

        const string initialRefresh =
            "initial-refresh";

        const string enrolledAccess =
            "enrolled-access";

        const string enrolledRefresh =
            "enrolled-refresh";

        using var handler =
            new CapturingHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"tokenType\":\"Bearer\",\"accessToken\":\"{initialAccess}\",\"expiresIn\":900,\"refreshToken\":\"{initialRefresh}\"}}"),
                JsonResponse(
                    HttpStatusCode.OK,
                    $"{{\"tokenType\":\"Bearer\",\"accessToken\":\"{enrolledAccess}\",\"expiresIn\":900,\"refreshToken\":\"{enrolledRefresh}\"}}"));

        using var factory =
            new SingleClientFactory(
                handler);

        var authentication =
            new CapturingAuthenticationService(
                AuthenticateResult.NoResult());

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new WebAuthenticationService(
                factory);

        var result =
            await service.LoginAsync(
                context,
                "user@fullworth.local",
                "FullWorth!Tests123",
                rememberMe:
                    false);

        Assert.True(
            result.Succeeded);

        Assert.Collection(
            handler.Requests,
            login =>
            {
                Assert.Equal(
                    "/api/auth/login",
                    login.Path);
            },
            enroll =>
            {
                Assert.Equal(
                    "/api/auth/refresh",
                    enroll.Path);

                Assert.Contains(
                    initialRefresh,
                    enroll.Body,
                    StringComparison.Ordinal);
            });

        Assert.NotNull(
            authentication.LastSignInProperties);

        Assert.Equal(
            enrolledAccess,
            authentication.LastSignInProperties!
                .GetTokenValue(
                    "access_token"));

        Assert.Equal(
            enrolledRefresh,
            authentication.LastSignInProperties
                .GetTokenValue(
                    "refresh_token"));
    }

    [Fact]
    public async Task Logout_RevokesServerFamilyBeforeLocalSignOut()
    {
        const string refreshToken =
            "current-refresh-family-token";

        using var handler =
            new CapturingHandler(
                new HttpResponseMessage(
                    HttpStatusCode.NoContent));

        using var factory =
            new SingleClientFactory(
                handler);

        var authentication =
            CreateAuthenticatedSession(
                refreshToken);

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new WebAuthenticationService(
                factory);

        await service.LogoutAsync(
            context);

        var revoke =
            Assert.Single(
                handler.Requests);

        Assert.Equal(
            HttpMethod.Post,
            revoke.Method);

        Assert.Equal(
            "/api/auth/logout",
            revoke.Path);

        Assert.Contains(
            refreshToken,
            revoke.Body,
            StringComparison.Ordinal);

        Assert.True(
            authentication.SignedOut);
    }

    [Fact]
    public async Task Logout_ServerUnavailable_StillSignsOutLocally()
    {
        using var factory =
            new SingleClientFactory(
                new ThrowingHandler());

        var authentication =
            CreateAuthenticatedSession(
                "current-refresh-family-token");

        var context =
            CreateHttpContext(
                authentication);

        var service =
            new WebAuthenticationService(
                factory);

        await service.LogoutAsync(
            context);

        Assert.True(
            authentication.SignedOut);
    }

    private static CapturingAuthenticationService
        CreateAuthenticatedSession(
            string refreshToken)
    {
        var identity =
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        "logout-security-test")
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
                        "current-access-token"
                },

                new AuthenticationToken
                {
                    Name =
                        "refresh_token",

                    Value =
                        refreshToken
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

    private static DefaultHttpContext CreateHttpContext(
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

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(
            statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
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

    private sealed class CapturingHandler(
        params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage>
            _responses =
                new(
                    responses);

        public List<CapturedRequest> Requests { get; } =
            [];

        protected override async Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scripted response remains.");
            }

            var body =
                request.Content is null
                    ? string.Empty
                    : await request.Content
                        .ReadAsStringAsync(
                            cancellationToken);

            Requests.Add(
                new CapturedRequest(
                    request.Method,
                    request.RequestUri?.AbsolutePath
                    ?? throw new InvalidOperationException(
                        "Request URI was missing."),
                    body));

            return _responses.Dequeue();
        }
    }

    private sealed class ThrowingHandler
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            throw new HttpRequestException(
                "Simulated logout transport failure.");
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string Body);

    private sealed class CapturingAuthenticationService(
        AuthenticateResult authenticateResult)
        : IAuthenticationService
    {
        public bool SignedOut { get; private set; }

        public AuthenticationProperties?
            LastSignInProperties { get; private set; }

        public Task<AuthenticateResult> AuthenticateAsync(
            HttpContext context,
            string? scheme)
        {
            Assert.Equal(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                scheme);

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
            Assert.Equal(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                scheme);

            LastSignInProperties =
                properties;

            return Task.CompletedTask;
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

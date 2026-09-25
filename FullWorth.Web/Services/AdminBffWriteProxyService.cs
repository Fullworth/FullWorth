using System.Net;
using FullWorth.Web.Infrastructure;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace FullWorth.Web.Services;

public sealed class AdminBffWriteProxyService(
    IHttpClientFactory httpClientFactory,
    IWebSessionTicketAccessor? sessionTicketAccessor = null)
{
    private const string SubscriptionCheckoutPath =
        "/api/subscription/checkout";

    private const string SubscriptionRedemptionPath =
        "/api/subscription/access-keys/redeem";

    private const string AccountPreferencesPath =
        "/api/account/preferences";

    private const string AccountSecurityPath =
        "/api/account/security";

    private const string PasswordChangePath =
        "/api/account/security/password";

    private const string ExternalIdentityLinkPath =
        "/api/auth/external/link";

    private const string ExternalIdentityUnlinkPath =
        "/api/auth/external/unlink";

    private const string AccountDeletionPath =
        "/api/account";

    private const string AccountExportPath =
        "/api/account/export";

    private static readonly TimeSpan RefreshBuffer =
        TimeSpan.FromMinutes(1);

    public Task<IResult> ForwardJsonAsync<T>(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        T body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(method);

        var isAccountDeletion =
            method == HttpMethod.Delete &&
            string.Equals(
                requestUri,
                AccountDeletionPath,
                StringComparison.Ordinal);

        var isPasswordChange =
            method == HttpMethod.Post &&
            string.Equals(
                requestUri,
                PasswordChangePath,
                StringComparison.Ordinal);

        if (method != HttpMethod.Post &&
            method != HttpMethod.Put &&
            !isAccountDeletion)
        {
            return Task.FromResult<IResult>(
                Results.BadRequest());
        }

        if (!IsAllowedApiPath(requestUri))
        {
            return Task.FromResult<IResult>(
                Results.BadRequest());
        }

        return ForwardCoreAsync(
            httpContext,
            method,
            requestUri,
            body,
            signOutOnSuccess:
                isAccountDeletion ||
                isPasswordChange,
            cancellationToken);
    }

    public async Task<IResult> ForwardJsonDownloadAsync<T>(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        T body,
        string downloadFileName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        if (method != HttpMethod.Post ||
            !string.Equals(
                requestUri,
                AccountExportPath,
                StringComparison.Ordinal) ||
            !IsAllowedApiPath(requestUri))
        {
            return Results.BadRequest();
        }

        var session =
            await GetValidSessionAsync(
                httpContext,
                cancellationToken);

        if (session is null)
        {
            return Results.Unauthorized();
        }

        var response =
            await SendAuthorizedJsonAsync(
                method,
                requestUri,
                session.AccessToken,
                body,
                cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            session =
                await TryRefreshAsync(
                    httpContext,
                    session,
                    cancellationToken);

            if (session is null)
            {
                return Results.Unauthorized();
            }

            response =
                await SendAuthorizedJsonAsync(
                    method,
                    requestUri,
                    session.AccessToken,
                    body,
                    cancellationToken);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ToResultAsync(
                    response,
                    cancellationToken);
            }

            var bytes =
                await response.Content.ReadAsByteArrayAsync(
                    cancellationToken);

            return Results.File(
                bytes,
                contentType,
                Path.GetFileName(downloadFileName));
        }
    }

    public Task<IResult> ForwardJsonAndSignOutOnSuccessAsync<T>(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        T body,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(method);

        if (method != HttpMethod.Post &&
            method != HttpMethod.Put)
        {
            return Task.FromResult<IResult>(
                Results.BadRequest());
        }

        if (!IsAllowedApiPath(requestUri))
        {
            return Task.FromResult<IResult>(
                Results.BadRequest());
        }

        return ForwardCoreAsync(
            httpContext,
            method,
            requestUri,
            body,
            signOutOnSuccess: true,
            cancellationToken);
    }

    private async Task<IResult> ForwardCoreAsync<T>(
        HttpContext httpContext,
        HttpMethod method,
        string requestUri,
        T body,
        bool signOutOnSuccess,
        CancellationToken cancellationToken)
    {
        var session = await GetValidSessionAsync(
            httpContext,
            cancellationToken);

        if (session is null)
        {
            return Results.Unauthorized();
        }

        var response = await SendAuthorizedJsonAsync(
            method,
            requestUri,
            session.AccessToken,
            body,
            cancellationToken);

        if (response.StatusCode ==
            HttpStatusCode.Unauthorized)
        {
            response.Dispose();

            session = await TryRefreshAsync(
                httpContext,
                session,
                cancellationToken);

            if (session is null)
            {
                return Results.Unauthorized();
            }

            response = await SendAuthorizedJsonAsync(
                method,
                requestUri,
                session.AccessToken,
                body,
                cancellationToken);
        }

        using (response)
        {
            if (signOutOnSuccess &&
                response.IsSuccessStatusCode)
            {
                await httpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return await ToResultAsync(
                response,
                cancellationToken);
        }
    }

    private async Task<HttpResponseMessage>
        SendAuthorizedJsonAsync<T>(
        HttpMethod method,
        string requestUri,
        string accessToken,
        T body,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(
            "FullWorthApi");

        using var request = new HttpRequestMessage(
            method,
            requestUri);

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                accessToken);

        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(
                "application/json"));

        request.Content = JsonContent.Create(body);

        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private async Task<WebApiSession?> GetValidSessionAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var session = await GetSessionAsync(httpContext);

        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAtUtc is null ||
            session.ExpiresAtUtc.Value >
                DateTimeOffset.UtcNow.Add(RefreshBuffer))
        {
            return session;
        }

        return await TryRefreshAsync(
            httpContext,
            session,
            cancellationToken);
    }

    private static async Task<WebApiSession?> GetSessionAsync(
        HttpContext httpContext)
    {
        var authenticateResult =
            await httpContext.AuthenticateAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);

        if (!authenticateResult.Succeeded ||
            authenticateResult.Principal is null ||
            authenticateResult.Properties is null)
        {
            return null;
        }

        var accessToken =
            authenticateResult.Properties.GetTokenValue(
                "access_token");

        var refreshToken =
            authenticateResult.Properties.GetTokenValue(
                "refresh_token");

        var expiresAtText =
            authenticateResult.Properties.GetTokenValue(
                "expires_at");

        if (string.IsNullOrWhiteSpace(accessToken) ||
            string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        DateTimeOffset? expiresAtUtc = null;

        if (DateTimeOffset.TryParse(
                expiresAtText,
                out var parsedExpiresAt))
        {
            expiresAtUtc = parsedExpiresAt;
        }

        return new WebApiSession(
            authenticateResult.Principal,
            authenticateResult.Properties,
            accessToken,
            refreshToken,
            expiresAtUtc);
    }

    private async Task<WebApiSession?> TryRefreshAsync(
        HttpContext httpContext,
        WebApiSession session,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(
            "FullWorthApi");

        using var response = await client.PostAsJsonAsync(
            "/api/auth/refresh",
            new
            {
                refreshToken = session.RefreshToken
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var concurrentlyRotatedSession =
                await TryRecoverConcurrentRefreshAsync(
                    session,
                    cancellationToken);

            if (concurrentlyRotatedSession is not null)
            {
                return concurrentlyRotatedSession;
            }

            if (sessionTicketAccessor is null ||
                !session.Properties.Items.ContainsKey(
                    WebSessionTicketAccessor.SessionKeyItem))
            {
                await httpContext.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
            }

            /*
             * A distributed winner may still be between receiving its rotated
             * API pair and renewing Redis. Do not delete the shared session
             * here; returning unauthorized is fail-closed and allows a later
             * request to observe the winner's renewed ticket.
             */
            return null;
        }

        var refreshedTokens =
            await response.Content.ReadFromJsonAsync<
                RefreshedTokenResponse>(
                cancellationToken:
                    cancellationToken);

        if (refreshedTokens is null ||
            string.IsNullOrWhiteSpace(refreshedTokens.AccessToken) ||
            string.IsNullOrWhiteSpace(refreshedTokens.RefreshToken))
        {
            await httpContext.SignOutAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            return null;
        }

        var expiresAtUtc = DateTimeOffset.UtcNow
            .AddSeconds(
                Math.Max(
                    refreshedTokens.ExpiresIn,
                    0));

        var existingTokens = session.Properties
            .GetTokens()
            .Where(
                token => token.Name is not
                    "access_token" and not
                    "refresh_token" and not
                    "expires_at" and not
                    "token_type")
            .ToList();

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "access_token",
                Value = refreshedTokens.AccessToken
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "refresh_token",
                Value = refreshedTokens.RefreshToken
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "expires_at",
                Value = expiresAtUtc.ToString("O")
            });

        existingTokens.Add(
            new AuthenticationToken
            {
                Name = "token_type",
                Value = refreshedTokens.TokenType
            });

        session.Properties.StoreTokens(existingTokens);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            session.Principal,
            session.Properties);

        return new WebApiSession(
            session.Principal,
            session.Properties,
            refreshedTokens.AccessToken,
            refreshedTokens.RefreshToken,
            expiresAtUtc);
    }

    private async Task<WebApiSession?>
        TryRecoverConcurrentRefreshAsync(
            WebApiSession session,
            CancellationToken cancellationToken)
    {
        if (sessionTicketAccessor is null)
        {
            return null;
        }

        for (var attempt = 0;
             attempt < 10;
             attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(50),
                    cancellationToken);
            }

            var latest =
                await sessionTicketAccessor
                    .ReadLatestAsync(
                        session.Properties,
                        cancellationToken);

            if (latest is null ||
                string.Equals(
                    latest.RefreshToken,
                    session.RefreshToken,
                    StringComparison.Ordinal))
            {
                continue;
            }

            return new WebApiSession(
                latest.Principal,
                latest.Properties,
                latest.AccessToken,
                latest.RefreshToken,
                latest.ExpiresAtUtc);
        }

        return null;
    }

    private static bool IsAllowedApiPath(
        string requestUri)
    {
        if (string.IsNullOrWhiteSpace(requestUri))
        {
            return false;
        }

        var isAdminPath =
            requestUri.StartsWith(
                "/api/admin/",
                StringComparison.Ordinal);

        var isSubscriptionCheckoutPath =
            string.Equals(
                requestUri,
                SubscriptionCheckoutPath,
                StringComparison.Ordinal);

        var isSubscriptionRedemptionPath =
            string.Equals(
                requestUri,
                SubscriptionRedemptionPath,
                StringComparison.Ordinal);

        var isAccountPreferencesPath =
            string.Equals(
                requestUri,
                AccountPreferencesPath,
                StringComparison.Ordinal);

        var isAccountSecurityPath =
            string.Equals(
                requestUri,
                AccountSecurityPath,
                StringComparison.Ordinal) ||
            requestUri.StartsWith(
                AccountSecurityPath + "/",
                StringComparison.Ordinal);

        var isExternalIdentityLinkPath =
            string.Equals(
                requestUri,
                ExternalIdentityLinkPath,
                StringComparison.Ordinal);

        var isExternalIdentityUnlinkPath =
            string.Equals(
                requestUri,
                ExternalIdentityUnlinkPath,
                StringComparison.Ordinal);

        var isAccountDeletionPath =
            string.Equals(
                requestUri,
                AccountDeletionPath,
                StringComparison.Ordinal);

        var isAccountExportPath =
            string.Equals(
                requestUri,
                AccountExportPath,
                StringComparison.Ordinal);

        if (!isAdminPath &&
            !isSubscriptionCheckoutPath &&
            !isSubscriptionRedemptionPath &&
            !isAccountPreferencesPath &&
            !isAccountSecurityPath &&
            !isExternalIdentityLinkPath &&
            !isExternalIdentityUnlinkPath &&
            !isAccountDeletionPath &&
            !isAccountExportPath)
        {
            return false;
        }

        return !requestUri.Contains(
                   "://",
                   StringComparison.Ordinal) &&
               !requestUri.Contains(
                   '\\',
                   StringComparison.Ordinal);
    }

    private static async Task<IResult> ToResultAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;

        if (response.StatusCode ==
            HttpStatusCode.NoContent)
        {
            return Results.StatusCode(statusCode);
        }

        var content = await response.Content
            .ReadAsStringAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            return Results.StatusCode(statusCode);
        }

        var contentType = response.Content.Headers
            .ContentType?
            .ToString()
            ?? "application/json; charset=utf-8";

        return Results.Content(
            content,
            contentType,
            Encoding.UTF8,
            statusCode);
    }

    private sealed record WebApiSession(
        System.Security.Claims.ClaimsPrincipal Principal,
        AuthenticationProperties Properties,
        string AccessToken,
        string RefreshToken,
        DateTimeOffset? ExpiresAtUtc);

    private sealed record RefreshedTokenResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}

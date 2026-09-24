using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace FullWorth.Web.Infrastructure;

public sealed record WebSessionTicketSnapshot(
    System.Security.Claims.ClaimsPrincipal Principal,
    AuthenticationProperties Properties,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset? ExpiresAtUtc);

public interface IWebSessionTicketAccessor
{
    Task<WebSessionTicketSnapshot?> ReadLatestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default);
}

public sealed class WebSessionTicketAccessor(
    IOptionsMonitor<CookieAuthenticationOptions> cookieOptions,
    ProtectedDistributedTicketStore ticketStore)
    : IWebSessionTicketAccessor
{
    private const string SessionIdClaim =
        "Microsoft.AspNetCore.Authentication.Cookies-SessionId";

    public async Task<WebSessionTicketSnapshot?> ReadLatestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            httpContext);

        var options =
            cookieOptions.Get(
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var cookieName =
            options.Cookie.Name;

        if (string.IsNullOrWhiteSpace(
                cookieName))
        {
            return null;
        }

        var cookie =
            options.CookieManager.GetRequestCookie(
                httpContext,
                cookieName);

        if (string.IsNullOrWhiteSpace(
                cookie))
        {
            return null;
        }

        var sessionReferenceTicket =
            options.TicketDataFormat.Unprotect(
                cookie);

        var sessionKey =
            sessionReferenceTicket?
                .Principal
                .Claims
                .FirstOrDefault(
                    claim =>
                        string.Equals(
                            claim.Type,
                            SessionIdClaim,
                            StringComparison.Ordinal))?
                .Value;

        if (string.IsNullOrWhiteSpace(
                sessionKey))
        {
            return null;
        }

        var latestTicket =
            await ticketStore.RetrieveAsync(
                sessionKey,
                cancellationToken);

        if (latestTicket is null)
        {
            return null;
        }

        var accessToken =
            latestTicket.Properties
                .GetTokenValue(
                    "access_token");

        var refreshToken =
            latestTicket.Properties
                .GetTokenValue(
                    "refresh_token");

        if (string.IsNullOrWhiteSpace(
                accessToken) ||
            string.IsNullOrWhiteSpace(
                refreshToken))
        {
            return null;
        }

        DateTimeOffset? expiresAtUtc =
            null;

        var expiresAtText =
            latestTicket.Properties
                .GetTokenValue(
                    "expires_at");

        if (DateTimeOffset.TryParse(
                expiresAtText,
                out var parsedExpiresAt))
        {
            expiresAtUtc =
                parsedExpiresAt;
        }

        return new WebSessionTicketSnapshot(
            latestTicket.Principal,
            latestTicket.Properties,
            accessToken,
            refreshToken,
            expiresAtUtc);
    }
}

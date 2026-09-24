using Microsoft.AspNetCore.Authentication;

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
        AuthenticationProperties currentProperties,
        CancellationToken cancellationToken = default);
}

public sealed class WebSessionTicketAccessor(
    ProtectedDistributedTicketStore ticketStore)
    : IWebSessionTicketAccessor
{
    public const string SessionKeyItem =
        "FullWorth.Web.SessionStoreKey";

    public async Task<WebSessionTicketSnapshot?> ReadLatestAsync(
        AuthenticationProperties currentProperties,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            currentProperties);

        if (!currentProperties.Items.TryGetValue(
                SessionKeyItem,
                out var sessionKey) ||
            string.IsNullOrWhiteSpace(
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

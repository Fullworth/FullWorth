using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FullWorth.API.Services.Identity;

public sealed class RefreshTokenReplayGuard(
    FullWorthDbContext dbContext,
    SignInManager<ApplicationUser> signInManager,
    IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
    TimeProvider timeProvider)
{
    internal const string TokenProvider =
        "FullWorth.RefreshReplay";

    public async Task<bool> TryConsumeAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                refreshToken))
        {
            return false;
        }

        var refreshTokenProtector =
            bearerTokenOptions
                .Get(
                    IdentityConstants.BearerScheme)
                .RefreshTokenProtector;

        var refreshTicket =
            refreshTokenProtector.Unprotect(
                refreshToken);

        var nowUtc =
            timeProvider.GetUtcNow();

        if (refreshTicket?.Properties?.ExpiresUtc is not
                { } expiresAtUtc ||
            nowUtc >= expiresAtUtc)
        {
            return false;
        }

        var user =
            await signInManager.ValidateSecurityStampAsync(
                refreshTicket.Principal);

        if (user is null)
        {
            return false;
        }

        var tokenHash =
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            refreshToken)))
                .ToLowerInvariant();

        var markerName =
            string.Create(
                CultureInfo.InvariantCulture,
                $"{expiresAtUtc.ToUnixTimeSeconds()}:{tokenHash}");

        var existingMarkers =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.UserId == user.Id &&
                        token.LoginProvider ==
                            TokenProvider)
                .ToListAsync(
                    cancellationToken);

        if (existingMarkers.Any(
                token =>
                    string.Equals(
                        token.Name,
                        markerName,
                        StringComparison.Ordinal)))
        {
            return false;
        }

        var nowUnixSeconds =
            nowUtc.ToUnixTimeSeconds();

        foreach (var marker in existingMarkers)
        {
            if (TryReadExpiryUnixSeconds(
                    marker.Name,
                    out var markerExpiry) &&
                markerExpiry <=
                    nowUnixSeconds)
            {
                dbContext.UserTokens.Remove(
                    marker);
            }
        }

        var consumedMarker =
            new IdentityUserToken<Guid>
            {
                UserId =
                    user.Id,

                LoginProvider =
                    TokenProvider,

                Name =
                    markerName,

                Value =
                    null
            };

        dbContext.UserTokens.Add(
            consumedMarker);

        try
        {
            await dbContext.SaveChangesAsync(
                cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is
                PostgresException postgresException &&
                postgresException.SqlState ==
                    PostgresErrorCodes.UniqueViolation)
        {
            /*
             * Concurrent refresh attempts can both pass the read check, but
             * AspNetUserTokens has a composite primary key. Exactly one
             * insert wins in PostgreSQL; the loser is a replay.
             */
            dbContext.Entry(
                    consumedMarker)
                .State =
                    EntityState.Detached;

            return false;
        }
    }

    private static bool TryReadExpiryUnixSeconds(
        string? markerName,
        out long expiresAtUnixSeconds)
    {
        expiresAtUnixSeconds =
            0;

        if (string.IsNullOrWhiteSpace(
                markerName))
        {
            return false;
        }

        var separatorIndex =
            markerName.IndexOf(
                ':');

        return separatorIndex > 0 &&
               long.TryParse(
                   markerName.AsSpan(
                       0,
                       separatorIndex),
                   NumberStyles.None,
                   CultureInfo.InvariantCulture,
                   out expiresAtUnixSeconds);
    }
}

public sealed class RefreshTokenReplayEndpointFilter(
    RefreshTokenReplayGuard replayGuard)
    : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var refreshRequest =
            context.Arguments
                .OfType<RefreshRequest>()
                .FirstOrDefault();

        if (refreshRequest is null)
        {
            return await next(
                context);
        }

        var accepted =
            await replayGuard.TryConsumeAsync(
                refreshRequest.RefreshToken,
                context.HttpContext.RequestAborted);

        if (!accepted)
        {
            return Results.Unauthorized();
        }

        return await next(
            context);
    }
}

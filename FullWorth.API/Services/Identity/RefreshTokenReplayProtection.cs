using System.Security.Cryptography;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullWorth.API.Services.Identity;

public sealed class RefreshTokenRotationService(
    FullWorthDbContext dbContext,
    SignInManager<ApplicationUser> signInManager,
    IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
    TimeProvider timeProvider)
{
    private const string TokenProvider =
        "FullWorth.RefreshFamily";

    private const string FamilyProperty =
        "fullworth:refresh_family";

    public async Task<AccessTokenResponse?> RotateAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                refreshToken))
        {
            return null;
        }

        var options =
            bearerTokenOptions.Get(
                IdentityConstants.BearerScheme);

        var refreshTicket =
            options.RefreshTokenProtector
                .Unprotect(
                    refreshToken);

        var nowUtc =
            timeProvider.GetUtcNow();

        if (refreshTicket?.Properties?.ExpiresUtc is not
                { } refreshExpiresAtUtc ||
            nowUtc >= refreshExpiresAtUtc)
        {
            return null;
        }

        var user =
            await signInManager.ValidateSecurityStampAsync(
                refreshTicket.Principal);

        if (user is null)
        {
            return null;
        }

        var presentedHash =
            HashToken(
                refreshToken);

        var familyId =
            refreshTicket.Properties.Items
                .TryGetValue(
                    FamilyProperty,
                    out var existingFamilyId) &&
            !string.IsNullOrWhiteSpace(
                existingFamilyId)
                ? existingFamilyId
                : presentedHash;

        if (!IsValidFamilyId(
                familyId))
        {
            return null;
        }

        var accessProperties =
            new AuthenticationProperties
            {
                ExpiresUtc =
                    nowUtc +
                    options.BearerTokenExpiration
            };

        var accessTicket =
            new AuthenticationTicket(
                refreshTicket.Principal,
                accessProperties,
                $"{IdentityConstants.BearerScheme}:AccessToken");

        var rotatedRefreshProperties =
            new AuthenticationProperties
            {
                ExpiresUtc =
                    nowUtc +
                    options.RefreshTokenExpiration
            };

        rotatedRefreshProperties.Items[
            FamilyProperty] =
                familyId;

        var rotatedRefreshTicket =
            new AuthenticationTicket(
                refreshTicket.Principal,
                rotatedRefreshProperties,
                $"{IdentityConstants.BearerScheme}:RefreshToken");

        var rotatedRefreshToken =
            options.RefreshTokenProtector
                .Protect(
                    rotatedRefreshTicket);

        var rotatedHash =
            HashToken(
                rotatedRefreshToken);

        /*
         * One bounded row represents the latest token in this refresh
         * family. The conditional UPSERT is the replay/concurrency barrier:
         * the first request either creates the family or advances it from
         * exactly the presented token hash. A concurrent or later replay
         * affects zero rows and is rejected.
         *
         * Initial framework-issued refresh tokens have no family property.
         * Their own SHA-256 hash becomes the deterministic family key, so
         * concurrent first refreshes still contend on the same row.
         */
        var advanced =
            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO "AspNetUserTokens"
                        ("UserId", "LoginProvider", "Name", "Value")
                    VALUES
                        ({user.Id}, {TokenProvider}, {familyId}, {rotatedHash})
                    ON CONFLICT ("UserId", "LoginProvider", "Name")
                    DO UPDATE SET
                        "Value" = EXCLUDED."Value"
                    WHERE
                        "AspNetUserTokens"."Value" = {presentedHash};
                    """,
                    cancellationToken);

        if (advanced != 1)
        {
            return null;
        }

        var accessToken =
            options.BearerTokenProtector
                .Protect(
                    accessTicket);

        return new AccessTokenResponse
        {
            AccessToken =
                accessToken,

            ExpiresIn =
                (long)options
                    .BearerTokenExpiration
                    .TotalSeconds,

            RefreshToken =
                rotatedRefreshToken
        };
    }

    private static string HashToken(
        string token)
    {
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        token)))
            .ToLowerInvariant();
    }

    private static bool IsValidFamilyId(
        string familyId)
    {
        if (familyId.Length != 64)
        {
            return false;
        }

        foreach (var character in familyId)
        {
            if (!char.IsAsciiHexDigit(
                    character))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed class RefreshTokenRotationEndpointFilter(
    RefreshTokenRotationService rotationService)
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

        var rotated =
            await rotationService.RotateAsync(
                refreshRequest.RefreshToken,
                context.HttpContext.RequestAborted);

        return rotated is null
            ? Results.Unauthorized()
            : Results.Ok(
                rotated);
    }
}

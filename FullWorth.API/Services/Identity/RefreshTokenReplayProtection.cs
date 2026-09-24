using System.Data;
using System.Globalization;
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

    private const string StateVersion =
        "v2";

    private const int MaxActiveFamiliesPerUser =
        16;

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

        if (!dbContext.Database.IsRelational())
        {
            return await RotateCoreAsync(
                user,
                refreshTicket,
                refreshToken,
                options,
                nowUtc,
                cancellationToken);
        }

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);

        /*
         * Serialize refresh-family mutations per Identity user.
         *
         * PostgreSQL row locks also serialize against ordinary Identity user
         * updates that rotate SecurityStamp. That gives a deterministic
         * ordering between token rotation and password/role/2FA/provider
         * security changes instead of allowing both to commit as if they
         * happened first.
         */
        _ =
            await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $"""
                    SELECT 1
                    FROM "AspNetUsers"
                    WHERE "Id" = {user.Id}
                    FOR UPDATE;
                    """,
                    cancellationToken);

        await dbContext.Entry(
                user)
            .ReloadAsync(
                cancellationToken);

        user =
            await signInManager.ValidateSecurityStampAsync(
                refreshTicket.Principal);

        if (user is null)
        {
            return null;
        }

        var response =
            await RotateCoreAsync(
                user,
                refreshTicket,
                refreshToken,
                options,
                nowUtc,
                cancellationToken);

        /*
         * RotateCore may prune expired or security-invalid family rows even
         * when the presented token itself is rejected. Commit that safe
         * cleanup while the per-user lock is still held.
         */
        await transaction.CommitAsync(
            cancellationToken);

        return response;
    }

    private async Task<AccessTokenResponse?> RotateCoreAsync(
        ApplicationUser user,
        AuthenticationTicket refreshTicket,
        string refreshToken,
        BearerTokenOptions options,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                user.SecurityStamp))
        {
            return null;
        }

        var securityStampHash =
            HashValue(
                user.SecurityStamp);

        var presentedHash =
            HashValue(
                refreshToken);

        refreshTicket.Properties.Items
            .TryGetValue(
                FamilyProperty,
                out var existingFamilyId);

        var isFirstFamilyRefresh =
            string.IsNullOrWhiteSpace(
                existingFamilyId);

        var familyId =
            isFirstFamilyRefresh
                ? presentedHash
                : existingFamilyId!;

        if (!IsValidHash(
                familyId))
        {
            return null;
        }

        var familyRows =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.UserId == user.Id &&
                        token.LoginProvider ==
                            TokenProvider)
                .ToListAsync(
                    cancellationToken);

        var activeFamilies =
            new Dictionary<
                string,
                IdentityUserToken<Guid>>(
                StringComparer.Ordinal);

        var pruned =
            false;

        foreach (var familyRow in familyRows)
        {
            if (!TryParseState(
                    familyRow.Value,
                    out _,
                    out var expiresAtUtc,
                    out var familySecurityStampHash) ||
                expiresAtUtc <=
                    nowUtc ||
                !FixedTimeHashEquals(
                    familySecurityStampHash,
                    securityStampHash))
            {
                dbContext.UserTokens.Remove(
                    familyRow);

                pruned =
                    true;

                continue;
            }

            activeFamilies[
                familyRow.Name] =
                    familyRow;
        }

        if (isFirstFamilyRefresh)
        {
            if (activeFamilies.ContainsKey(
                    familyId))
            {
                await SavePrunedRowsAsync(
                    pruned,
                    cancellationToken);

                return null;
            }

            /*
             * Never evict an active replay marker just to admit another
             * session. The original login-issued refresh token can remain
             * valid for its full lifetime, so removing its marker would make
             * that old token look like a new family again.
             */
            if (activeFamilies.Count >=
                MaxActiveFamiliesPerUser)
            {
                await SavePrunedRowsAsync(
                    pruned,
                    cancellationToken);

                return null;
            }
        }
        else
        {
            if (!activeFamilies.TryGetValue(
                    familyId,
                    out var family) ||
                !TryParseState(
                    family.Value,
                    out var persistedCurrentHash,
                    out _,
                    out var persistedSecurityStampHash) ||
                !FixedTimeHashEquals(
                    persistedCurrentHash,
                    presentedHash) ||
                !FixedTimeHashEquals(
                    persistedSecurityStampHash,
                    securityStampHash))
            {
                await SavePrunedRowsAsync(
                    pruned,
                    cancellationToken);

                return null;
            }
        }

        var principal =
            await signInManager.CreateUserPrincipalAsync(
                user);

        var refreshExpiresAtUtc =
            nowUtc +
            options.RefreshTokenExpiration;

        var accessTicket =
            new AuthenticationTicket(
                principal,
                new AuthenticationProperties
                {
                    ExpiresUtc =
                        nowUtc +
                        options.BearerTokenExpiration
                },
                $"{IdentityConstants.BearerScheme}:AccessToken");

        var rotatedRefreshProperties =
            new AuthenticationProperties
            {
                ExpiresUtc =
                    refreshExpiresAtUtc
            };

        rotatedRefreshProperties.Items[
            FamilyProperty] =
                familyId;

        var rotatedRefreshTicket =
            new AuthenticationTicket(
                principal,
                rotatedRefreshProperties,
                $"{IdentityConstants.BearerScheme}:RefreshToken");

        var rotatedRefreshToken =
            options.RefreshTokenProtector
                .Protect(
                    rotatedRefreshTicket);

        var nextStateValue =
            FormatState(
                HashValue(
                    rotatedRefreshToken),
                refreshExpiresAtUtc,
                securityStampHash);

        if (isFirstFamilyRefresh)
        {
            dbContext.UserTokens.Add(
                new IdentityUserToken<Guid>
                {
                    UserId =
                        user.Id,

                    LoginProvider =
                        TokenProvider,

                    Name =
                        familyId,

                    Value =
                        nextStateValue
                });
        }
        else
        {
            activeFamilies[
                    familyId]
                .Value =
                    nextStateValue;
        }

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return new AccessTokenResponse
        {
            AccessToken =
                options.BearerTokenProtector
                    .Protect(
                        accessTicket),

            ExpiresIn =
                (long)options
                    .BearerTokenExpiration
                    .TotalSeconds,

            RefreshToken =
                rotatedRefreshToken
        };
    }

    private async Task SavePrunedRowsAsync(
        bool pruned,
        CancellationToken cancellationToken)
    {
        if (!pruned)
        {
            return;
        }

        await dbContext.SaveChangesAsync(
            cancellationToken);
    }

    private static string FormatState(
        string currentTokenHash,
        DateTimeOffset expiresAtUtc,
        string securityStampHash)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{StateVersion}|{expiresAtUtc.ToUnixTimeSeconds()}|{currentTokenHash}|{securityStampHash}");
    }

    private static bool TryParseState(
        string? value,
        out string currentTokenHash,
        out DateTimeOffset expiresAtUtc,
        out string securityStampHash)
    {
        currentTokenHash =
            string.Empty;

        securityStampHash =
            string.Empty;

        expiresAtUtc =
            default;

        if (string.IsNullOrWhiteSpace(
                value))
        {
            return false;
        }

        var parts =
            value.Split(
                '|',
                4,
                StringSplitOptions.None);

        if (parts.Length !=
                4 ||
            !string.Equals(
                parts[0],
                StateVersion,
                StringComparison.Ordinal) ||
            !long.TryParse(
                parts[1],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var expiresAtUnixSeconds) ||
            !IsValidHash(
                parts[2]) ||
            !IsValidHash(
                parts[3]))
        {
            return false;
        }

        try
        {
            expiresAtUtc =
                DateTimeOffset.FromUnixTimeSeconds(
                    expiresAtUnixSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        currentTokenHash =
            parts[2];

        securityStampHash =
            parts[3];

        return true;
    }

    private static string HashValue(
        string value)
    {
        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        value)))
            .ToLowerInvariant();
    }

    private static bool FixedTimeHashEquals(
        string left,
        string right)
    {
        if (!IsValidHash(
                left) ||
            !IsValidHash(
                right))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(
                left),
            Convert.FromHexString(
                right));
    }

    private static bool IsValidHash(
        string? value)
    {
        return value is
            { Length: 64 } &&
               value.All(
                   character =>
                       character is >= '0' and <= '9' ||
                       character is >= 'a' and <= 'f');
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

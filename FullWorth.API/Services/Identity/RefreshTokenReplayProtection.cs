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
using Npgsql;

namespace FullWorth.API.Services.Identity;

public sealed class RefreshTokenReplayGuard(
    FullWorthDbContext dbContext,
    SignInManager<ApplicationUser> signInManager,
    IOptionsMonitor<BearerTokenOptions> bearerTokenOptions,
    TimeProvider timeProvider)
{
    private const string TokenProvider =
        "FullWorth.RefreshFamily";

    private const string FamilyItemKey =
        "FullWorth.RefreshFamilyId";

    private const string StateVersion =
        "v2";

    private const int MaxActiveFamiliesPerUser =
        16;

    public async Task<AccessTokenResponse?>
        TryRotateAsync(
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
                { } presentedExpiresAtUtc ||
            nowUtc >= presentedExpiresAtUtc)
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

        if (string.IsNullOrWhiteSpace(
                user.SecurityStamp))
        {
            return null;
        }

        var securityStampHash =
            HashToken(
                user.SecurityStamp);

        var presentedHash =
            HashToken(
                refreshToken);

        refreshTicket.Properties.Items
            .TryGetValue(
                FamilyItemKey,
                out var suppliedFamilyKey);

        var isFirstFamilyRefresh =
            string.IsNullOrWhiteSpace(
                suppliedFamilyKey);

        var familyKey =
            isFirstFamilyRefresh
                ? presentedHash
                : suppliedFamilyKey!;

        if (!IsValidHash(
                familyKey))
        {
            return null;
        }

        if (!isFirstFamilyRefresh)
        {
            var currentStateValue =
                await dbContext.UserTokens
                    .AsNoTracking()
                    .Where(
                        token =>
                            token.UserId == user.Id &&
                            token.LoginProvider ==
                                TokenProvider &&
                            token.Name ==
                                familyKey)
                    .Select(
                        token =>
                            token.Value)
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (!TryParseState(
                    currentStateValue,
                    out var currentHash,
                    out var familyExpiresAtUtc,
                    out var persistedSecurityStampHash) ||
                familyExpiresAtUtc <=
                    nowUtc ||
                !FixedTimeHashEquals(
                    persistedSecurityStampHash,
                    securityStampHash) ||
                !FixedTimeHashEquals(
                    currentHash,
                    presentedHash))
            {
                return null;
            }
        }

        var principal =
            await signInManager.CreateUserPrincipalAsync(
                user);

        var rotated =
            CreateRotatedTokens(
                options,
                principal,
                familyKey,
                nowUtc);

        var rotatedHash =
            HashToken(
                rotated.Response.RefreshToken);

        var nextStateValue =
            FormatState(
                rotatedHash,
                rotated.RefreshExpiresAtUtc,
                securityStampHash);

        if (isFirstFamilyRefresh)
        {
            var accepted =
                await TryCreateFamilyAsync(
                    user.Id,
                    familyKey,
                    nextStateValue,
                    securityStampHash,
                    nowUtc,
                    cancellationToken);

            return accepted
                ? rotated.Response
                : null;
        }

        var updated =
            await TryAdvanceFamilyAsync(
                user.Id,
                familyKey,
                presentedHash,
                nextStateValue,
                cancellationToken);

        return updated
            ? rotated.Response
            : null;
    }

    private async Task<bool> TryCreateFamilyAsync(
        Guid userId,
        string familyKey,
        string stateValue,
        string currentSecurityStampHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        var existingFamily =
            await dbContext.UserTokens
                .AsNoTracking()
                .AnyAsync(
                    token =>
                        token.UserId == userId &&
                        token.LoginProvider ==
                            TokenProvider &&
                        token.Name ==
                            familyKey,
                    cancellationToken);

        if (existingFamily)
        {
            return false;
        }

        var familyRows =
            await dbContext.UserTokens
                .Where(
                    token =>
                        token.UserId == userId &&
                        token.LoginProvider ==
                            TokenProvider)
                .ToListAsync(
                    cancellationToken);

        var activeFamilies =
            new List<(
                IdentityUserToken<Guid> Token,
                DateTimeOffset ExpiresAtUtc)>();

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
                    currentSecurityStampHash))
            {
                dbContext.UserTokens.Remove(
                    familyRow);

                continue;
            }

            activeFamilies.Add(
                (
                    familyRow,
                    expiresAtUtc));
        }

        if (activeFamilies.Count >=
            MaxActiveFamiliesPerUser)
        {
            /*
             * Never evict an active replay record merely to make room. Its
             * original login-issued refresh token can still be inside its
             * validity window and, because that token predates family
             * metadata, deleting the row could let it establish the family
             * again. Reject creation until an existing family expires.
             */
            if (dbContext.ChangeTracker.HasChanges())
            {
                await dbContext.SaveChangesAsync(
                    cancellationToken);
            }

            return false;
        }

        dbContext.UserTokens.Add(
            new IdentityUserToken<Guid>
            {
                UserId =
                    userId,

                LoginProvider =
                    TokenProvider,

                Name =
                    familyKey,

                Value =
                    stateValue
            });

        try
        {
            await dbContext.SaveChangesAsync(
                cancellationToken);

            return true;
        }
        catch (DbUpdateException exception)
            when (IsUniqueViolation(
                exception))
        {
            return false;
        }
    }

    private async Task<bool> TryAdvanceFamilyAsync(
        Guid userId,
        string familyKey,
        string expectedCurrentHash,
        string nextStateValue,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var currentStatePrefix =
                $"{StateVersion}|";

            /*
             * The value predicate is completed below after reading the
             * current row. ExecuteUpdate then performs compare-and-swap in
             * PostgreSQL so concurrent requests using the same token cannot
             * both advance a family.
             */
            var currentStateValue =
                await dbContext.UserTokens
                    .AsNoTracking()
                    .Where(
                        token =>
                            token.UserId == userId &&
                            token.LoginProvider ==
                                TokenProvider &&
                            token.Name ==
                                familyKey &&
                            token.Value != null &&
                            token.Value.StartsWith(
                                currentStatePrefix))
                    .Select(
                        token =>
                            token.Value)
                    .SingleOrDefaultAsync(
                        cancellationToken);

            if (!TryParseState(
                    currentStateValue,
                    out var relationalPersistedHash,
                    out _) ||
                !FixedTimeHashEquals(
                    relationalPersistedHash,
                    expectedCurrentHash))
            {
                return false;
            }

            var rowsUpdated =
                await dbContext.UserTokens
                    .Where(
                        token =>
                            token.UserId == userId &&
                            token.LoginProvider ==
                                TokenProvider &&
                            token.Name ==
                                familyKey &&
                            token.Value ==
                                currentStateValue)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                token =>
                                    token.Value,
                                nextStateValue),
                        cancellationToken);

            return rowsUpdated == 1;
        }

        /*
         * EF's in-memory provider is used by the security integration suite
         * and does not implement ExecuteUpdate. Sequential replay behavior is
         * still exercised there; PostgreSQL's conditional update is the
         * production concurrency barrier.
         */
        var family =
            await dbContext.UserTokens
                .SingleOrDefaultAsync(
                    token =>
                        token.UserId == userId &&
                        token.LoginProvider ==
                            TokenProvider &&
                        token.Name ==
                            familyKey,
                    cancellationToken);

        if (family is null ||
            !TryParseState(
                family.Value,
                out var inMemoryPersistedHash,
                out _) ||
            !FixedTimeHashEquals(
                inMemoryPersistedHash,
                expectedCurrentHash))
        {
            return false;
        }

        family.Value =
            nextStateValue;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private static RotatedTokens CreateRotatedTokens(
        BearerTokenOptions options,
        System.Security.Claims.ClaimsPrincipal principal,
        string familyKey,
        DateTimeOffset nowUtc)
    {
        var accessExpiresAtUtc =
            nowUtc +
            options.BearerTokenExpiration;

        var accessProperties =
            new AuthenticationProperties
            {
                ExpiresUtc =
                    accessExpiresAtUtc
            };

        var accessTicket =
            new AuthenticationTicket(
                principal,
                accessProperties,
                $"{IdentityConstants.BearerScheme}:AccessToken");

        var refreshExpiresAtUtc =
            nowUtc +
            options.RefreshTokenExpiration;

        var refreshProperties =
            new AuthenticationProperties
            {
                ExpiresUtc =
                    refreshExpiresAtUtc
            };

        refreshProperties.Items[
            FamilyItemKey] =
                familyKey;

        var refreshTicket =
            new AuthenticationTicket(
                principal,
                refreshProperties,
                $"{IdentityConstants.BearerScheme}:RefreshToken");

        var accessToken =
            options.BearerTokenProtector
                .Protect(
                    accessTicket);

        var rotatedRefreshToken =
            options.RefreshTokenProtector
                .Protect(
                    refreshTicket);

        return new RotatedTokens(
            new AccessTokenResponse
            {
                AccessToken =
                    accessToken,

                ExpiresIn =
                    (long)options
                        .BearerTokenExpiration
                        .TotalSeconds,

                RefreshToken =
                    rotatedRefreshToken
            },
            refreshExpiresAtUtc);
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

    private static bool FixedTimeHashEquals(
        string left,
        string right)
    {
        if (!IsValidHash(left) ||
            !IsValidHash(right))
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

    private static string FormatState(
        string currentHash,
        DateTimeOffset expiresAtUtc,
        string securityStampHash)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{StateVersion}|{expiresAtUtc.ToUnixTimeSeconds()}|{currentHash}|{securityStampHash}");
    }

    private static bool TryParseState(
        string? value,
        out string currentHash,
        out DateTimeOffset expiresAtUtc,
        out string securityStampHash)
    {
        currentHash =
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

        if (parts.Length != 4 ||
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

        currentHash =
            parts[2];

        securityStampHash =
            parts[3];

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

        return true;
    }

    private static bool IsUniqueViolation(
        DbUpdateException exception)
    {
        return exception.InnerException is
                   PostgresException postgresException &&
               postgresException.SqlState ==
                   PostgresErrorCodes.UniqueViolation;
    }

    private sealed record RotatedTokens(
        AccessTokenResponse Response,
        DateTimeOffset RefreshExpiresAtUtc);
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

        var rotated =
            await replayGuard.TryRotateAsync(
                refreshRequest.RefreshToken,
                context.HttpContext.RequestAborted);

        return rotated is null
            ? Results.Unauthorized()
            : Results.Ok(
                rotated);
    }
}

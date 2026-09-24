using System.Buffers.Binary;
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
                { } presentedExpiresAtUtc ||
            nowUtc >= presentedExpiresAtUtc)
        {
            return null;
        }

        var user =
            await signInManager.ValidateSecurityStampAsync(
                refreshTicket.Principal);

        if (user is null ||
            string.IsNullOrWhiteSpace(
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
                out var suppliedFamilyId);

        var isFirstFamilyRefresh =
            string.IsNullOrWhiteSpace(
                suppliedFamilyId);

        var familyId =
            isFirstFamilyRefresh
                ? presentedHash
                : suppliedFamilyId!;

        if (!IsValidHash(
                familyId))
        {
            return null;
        }

        var presentedState =
            FormatState(
                presentedHash,
                presentedExpiresAtUtc,
                securityStampHash);

        /*
         * Rebuild the principal from current Identity state instead of
         * carrying forward claims from the old refresh ticket. Role and
         * other Identity claim changes therefore flow into every rotated
         * access token.
         */
        var principal =
            await signInManager.CreateUserPrincipalAsync(
                user);

        var rotated =
            CreateRotatedTokens(
                options,
                principal,
                familyId,
                nowUtc);

        var rotatedHash =
            HashValue(
                rotated.Response.RefreshToken);

        var nextState =
            FormatState(
                rotatedHash,
                rotated.RefreshExpiresAtUtc,
                securityStampHash);

        var advanced =
            await PersistRotationAsync(
                user.Id,
                familyId,
                isFirstFamilyRefresh,
                presentedState,
                nextState,
                securityStampHash,
                nowUtc,
                cancellationToken);

        return advanced
            ? rotated.Response
            : null;
    }

    public async Task<bool> RevokeFamilyAsync(
        string? refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                refreshToken))
        {
            return false;
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

        refreshTicket.Properties.Items
            .TryGetValue(
                FamilyProperty,
                out var suppliedFamilyId);

        var hasExplicitFamily =
            !string.IsNullOrWhiteSpace(
                suppliedFamilyId);

        /*
         * Framework-issued login tokens do not contain a family identifier
         * until FullWorth performs their first rotation. Deriving the same
         * identifier used by RotateAsync lets an already-rotated family's
         * original generation revoke that family too.
         */
        var familyId =
            hasExplicitFamily
                ? suppliedFamilyId!
                : HashValue(
                    refreshToken);

        if (!IsValidHash(
                familyId))
        {
            return false;
        }

        if (!dbContext.Database.IsRelational())
        {
            var revoked =
                await DeleteFamilyAsync(
                    user.Id,
                    familyId,
                    cancellationToken);

            if (revoked ||
                hasExplicitFamily)
            {
                return revoked;
            }

            /*
             * A framework-issued first-generation token has no family row
             * until its first refresh. If logout arrives before enrollment,
             * fail secure by rotating the user's security stamp. That is
             * broader than current-session revocation, but it guarantees the
             * presented token cannot remain usable.
             */
            var stampResult =
                await signInManager.UserManager
                    .UpdateSecurityStampAsync(
                        user);

            return stampResult.Succeeded;
        }

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        var lockKey =
            CreateAdvisoryLockKey(
                user.Id);

        await dbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey});",
                cancellationToken);

        var revoked =
            await DeleteFamilyAsync(
                user.Id,
                familyId,
                cancellationToken);

        if (!revoked &&
            !hasExplicitFamily)
        {
            var stampResult =
                await signInManager.UserManager
                    .UpdateSecurityStampAsync(
                        user);

            revoked =
                stampResult.Succeeded;
        }

        await transaction.CommitAsync(
            cancellationToken);

        return revoked;
    }

    private async Task<bool> DeleteFamilyAsync(
        Guid userId,
        string familyId,
        CancellationToken cancellationToken)
    {
        var family =
            await dbContext.UserTokens
                .SingleOrDefaultAsync(
                    token =>
                        token.UserId == userId &&
                        token.LoginProvider ==
                            TokenProvider &&
                        token.Name ==
                            familyId,
                    cancellationToken);

        if (family is null)
        {
            return false;
        }

        /*
         * Any cryptographically valid generation from the same family may
         * revoke that family. This intentionally makes logout win against a
         * concurrent refresh rotation. Possession of a stale family token can
         * therefore only terminate that same session; it cannot mint tokens
         * or affect another refresh family.
         */
        dbContext.UserTokens.Remove(
            family);

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private async Task<bool> PersistRotationAsync(
        Guid userId,
        string familyId,
        bool isFirstFamilyRefresh,
        string presentedState,
        string nextState,
        string securityStampHash,
        DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        if (!dbContext.Database.IsRelational())
        {
            return isFirstFamilyRefresh
                ? await TryCreateFamilyAsync(
                    userId,
                    familyId,
                    nextState,
                    securityStampHash,
                    nowUtc,
                    cancellationToken)
                : await TryAdvanceFamilyAsync(
                    userId,
                    familyId,
                    presentedState,
                    nextState,
                    cancellationToken);
        }

        await using var transaction =
            await dbContext.Database
                .BeginTransactionAsync(
                    cancellationToken);

        /*
         * Serialize refresh-family mutations per user across API instances.
         * The database remains the source of truth; this is not an in-memory
         * lock that disappears when FullWorth scales horizontally.
         */
        var lockKey =
            CreateAdvisoryLockKey(
                userId);

        await dbContext.Database
            .ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({lockKey});",
                cancellationToken);

        var accepted =
            isFirstFamilyRefresh
                ? await TryCreateFamilyAsync(
                    userId,
                    familyId,
                    nextState,
                    securityStampHash,
                    nowUtc,
                    cancellationToken)
                : await TryAdvanceFamilyAsync(
                    userId,
                    familyId,
                    presentedState,
                    nextState,
                    cancellationToken);

        await transaction.CommitAsync(
            cancellationToken);

        return accepted;
    }

    private async Task<bool> TryCreateFamilyAsync(
        Guid userId,
        string familyId,
        string nextState,
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
                            familyId,
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

        var activeFamilyCount =
            0;

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
                /*
                 * Expired, malformed, and pre-security-change families no
                 * longer protect a valid refresh token. Remove them before
                 * enforcing the active-session ceiling.
                 */
                dbContext.UserTokens.Remove(
                    familyRow);

                continue;
            }

            activeFamilyCount++;
        }

        if (activeFamilyCount >=
            MaxActiveFamiliesPerUser)
        {
            /*
             * Never evict active replay state to admit another family. The
             * original framework-issued token for an evicted family might
             * still be valid and could otherwise establish itself again.
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
                    familyId,

                Value =
                    nextState
            });

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private async Task<bool> TryAdvanceFamilyAsync(
        Guid userId,
        string familyId,
        string expectedState,
        string nextState,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            /*
             * Exact-state compare-and-swap is a second replay barrier beneath
             * the per-user PostgreSQL advisory lock. A stale generation can
             * never advance a family.
             */
            var rowsUpdated =
                await dbContext.UserTokens
                    .Where(
                        token =>
                            token.UserId == userId &&
                            token.LoginProvider ==
                                TokenProvider &&
                            token.Name ==
                                familyId &&
                            token.Value ==
                                expectedState)
                    .ExecuteUpdateAsync(
                        setters =>
                            setters.SetProperty(
                                token =>
                                    token.Value,
                                nextState),
                        cancellationToken);

            return rowsUpdated == 1;
        }

        /*
         * The EF in-memory provider used by security integration tests does
         * not implement ExecuteUpdate. Sequential replay semantics are still
         * enforced by exact-state comparison here; production concurrency is
         * proven against PostgreSQL in the container gate.
         */
        var family =
            await dbContext.UserTokens
                .SingleOrDefaultAsync(
                    token =>
                        token.UserId == userId &&
                        token.LoginProvider ==
                            TokenProvider &&
                        token.Name ==
                            familyId,
                    cancellationToken);

        if (family is null ||
            !string.Equals(
                family.Value,
                expectedState,
                StringComparison.Ordinal))
        {
            return false;
        }

        family.Value =
            nextState;

        await dbContext.SaveChangesAsync(
            cancellationToken);

        return true;
    }

    private static RotatedTokens CreateRotatedTokens(
        BearerTokenOptions options,
        System.Security.Claims.ClaimsPrincipal principal,
        string familyId,
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
            FamilyProperty] =
                familyId;

        var refreshTicket =
            new AuthenticationTicket(
                principal,
                refreshProperties,
                $"{IdentityConstants.BearerScheme}:RefreshToken");

        return new RotatedTokens(
            new AccessTokenResponse
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
                    options.RefreshTokenProtector
                        .Protect(
                            refreshTicket)
            },
            refreshExpiresAtUtc);
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

        currentTokenHash =
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

    private static long CreateAdvisoryLockKey(
        Guid userId)
    {
        Span<byte> digest =
            stackalloc byte[32];

        SHA256.HashData(
            userId.ToByteArray(),
            digest);

        return BinaryPrimitives.ReadInt64BigEndian(
            digest[..8]);
    }

    private sealed record RotatedTokens(
        AccessTokenResponse Response,
        DateTimeOffset RefreshExpiresAtUtc);
}

public sealed record RefreshTokenLogoutRequest(
    string? RefreshToken);

public sealed class RefreshTokenReplayEndpointFilter(
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

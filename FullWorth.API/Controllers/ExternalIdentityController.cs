using FullWorth.API.Authorization;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Legal;
using FullWorth.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/auth/external")]
[EnableRateLimiting("authentication")]
[SubscriptionAccessExempt]
public sealed class ExternalIdentityController : ControllerBase
{
    private readonly UserManager<ApplicationUser>
        _userManager;

    private readonly SignInManager<ApplicationUser>
        _signInManager;

    private readonly IExternalIdentityTokenValidator
        _tokenValidator;

    private readonly ExternalIdentitySecondFactorVerifier
        _secondFactorVerifier;

    public ExternalIdentityController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IExternalIdentityTokenValidator tokenValidator)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(signInManager);
        ArgumentNullException.ThrowIfNull(tokenValidator);

        _userManager =
            userManager;

        _signInManager =
            signInManager;

        _tokenValidator =
            tokenValidator;

        _secondFactorVerifier =
            new ExternalIdentitySecondFactorVerifier(
                userManager);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] ExternalIdentityLoginRequest request,
        CancellationToken cancellationToken)
    {
        var externalIdentity =
            await ValidateIdentityAsync(
                request.Provider,
                request.IdToken,
                cancellationToken);

        if (externalIdentity is null)
        {
            return Unauthorized();
        }

        var user =
            await _userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        if (user is null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(
                user))
        {
            /*
             * Keep the failure intentionally indistinguishable from an
             * invalid provider token so callers cannot enumerate linked
             * external identities.
             */
            return Unauthorized();
        }

        /*
         * Provider authentication proves the external identity only.
         * FullWorth's own second factor remains mandatory when enabled.
         * Recovery codes are redeemed at this API boundary so they preserve
         * the same one-time semantics as password sign-in.
         */
        var secondFactorResult =
            await _secondFactorVerifier.VerifyAsync(
                user,
                request.TwoFactorCode,
                request.TwoFactorRecoveryCode);

        if (secondFactorResult ==
            ExternalIdentitySecondFactorResult.Required)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title:
                    "Additional verification required.",
                detail:
                    "RequiresTwoFactor");
        }

        if (secondFactorResult ==
            ExternalIdentitySecondFactorResult.Failed)
        {
            return Unauthorized();
        }

        user.LastLoginAtUtc =
            DateTimeOffset.UtcNow;

        var updateResult =
            await _userManager.UpdateAsync(
                user);

        if (!updateResult.Succeeded)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in is temporarily unavailable.");
        }

        /*
         * FullWorth's API authentication contract remains Identity bearer
         * tokens. Selecting the bearer scheme causes SignInManager to emit
         * the same access/refresh-token response format used by the normal
         * Identity API login endpoint.
         */
        _signInManager.AuthenticationScheme =
            IdentityConstants.BearerScheme;

        await _signInManager.SignInAsync(
            user,
            isPersistent:
                false);

        return new EmptyResult();
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register(
        [FromBody] ExternalIdentityRegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.AcceptedTermsAndPrivacy ||
            !string.Equals(
                request.LegalTermsVersion,
                FullWorthLegalDocuments.CurrentVersion,
                StringComparison.Ordinal))
        {
            return ValidationProblem(
                "Accept the current FullWorth Terms and Privacy Notice to create an account.");
        }

        var externalIdentity =
            await ValidateIdentityAsync(
                request.Provider,
                request.IdToken,
                cancellationToken);

        if (externalIdentity is null ||
            !externalIdentity.EmailVerified ||
            string.IsNullOrWhiteSpace(
                externalIdentity.Email))
        {
            return Unauthorized();
        }

        var email =
            externalIdentity.Email.Trim();

        var existingLogin =
            await _userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        var existingEmailOwner =
            await _userManager.FindByEmailAsync(
                email);

        if (existingLogin is not null ||
            existingEmailOwner is not null)
        {
            return ValidationProblem(
                "A FullWorth account already exists for this identity. Sign in and link the provider from Settings.");
        }

        var user =
            new ApplicationUser
            {
                UserName =
                    email,

                Email =
                    email,

                EmailConfirmed =
                    true
            };

        var createResult =
            await _userManager.CreateAsync(
                user,
                request.Password);

        if (!createResult.Succeeded)
        {
            return IdentityValidationProblem(
                createResult);
        }

        var addLoginResult =
            await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(
                    externalIdentity.Provider,
                    externalIdentity.Subject,
                    GetProviderDisplayName(
                        externalIdentity.Provider)));

        if (!addLoginResult.Succeeded)
        {
            var rollbackResult =
                await _userManager.DeleteAsync(
                    user);

            if (!rollbackResult.Succeeded)
            {
                return Problem(
                    statusCode:
                        StatusCodes.Status503ServiceUnavailable,
                    title:
                        "FullWorth could not safely finish external account creation.");
            }

            return IdentityValidationProblem(
                addLoginResult);
        }

        user.LastLoginAtUtc =
            DateTimeOffset.UtcNow;

        var updateResult =
            await _userManager.UpdateAsync(
                user);

        if (!updateResult.Succeeded)
        {
            await _userManager.DeleteAsync(
                user);

            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "FullWorth could not safely finish external account creation.");
        }

        _signInManager.AuthenticationScheme =
            IdentityConstants.BearerScheme;

        await _signInManager.SignInAsync(
            user,
            isPersistent:
                false);

        return new EmptyResult();
    }

    [HttpPost("link")]
    [Authorize]
    public async Task<IActionResult> Link(
        [FromBody] ExternalIdentityLinkRequest request,
        CancellationToken cancellationToken)
    {
        var user =
            await GetActiveUserAsync();

        if (user is null)
        {
            return Unauthorized();
        }

        if (!await ReauthenticateAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode,
                request.TwoFactorRecoveryCode))
        {
            return Unauthorized();
        }

        var externalIdentity =
            await ValidateIdentityAsync(
                request.Provider,
                request.IdToken,
                cancellationToken);

        if (externalIdentity is null)
        {
            return BadRequest(
                new
                {
                    error =
                        "ExternalIdentityInvalid"
                });
        }

        var existingOwner =
            await _userManager.FindByLoginAsync(
                externalIdentity.Provider,
                externalIdentity.Subject);

        if (existingOwner is not null &&
            existingOwner.Id != user.Id)
        {
            return Conflict(
                new
                {
                    error =
                        "ExternalIdentityAlreadyLinked"
                });
        }

        var currentLogins =
            await _userManager.GetLoginsAsync(
                user);

        var existingProviderLogin =
            currentLogins.FirstOrDefault(
                login =>
                    string.Equals(
                        login.LoginProvider,
                        externalIdentity.Provider,
                        StringComparison.OrdinalIgnoreCase));

        if (existingProviderLogin is not null)
        {
            if (string.Equals(
                    existingProviderLogin.ProviderKey,
                    externalIdentity.Subject,
                    StringComparison.Ordinal))
            {
                return NoContent();
            }

            return Conflict(
                new
                {
                    error =
                        "ExternalProviderAlreadyLinked"
                });
        }

        var addLoginResult =
            await _userManager.AddLoginAsync(
                user,
                new UserLoginInfo(
                    externalIdentity.Provider,
                    externalIdentity.Subject,
                    GetProviderDisplayName(
                        externalIdentity.Provider)));

        if (!addLoginResult.Succeeded)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in could not be linked.");
        }

        return NoContent();
    }

    [HttpPost("unlink")]
    [Authorize]
    public async Task<IActionResult> Unlink(
        [FromBody] ExternalIdentityUnlinkRequest request)
    {
        var user =
            await GetActiveUserAsync();

        if (user is null)
        {
            return Unauthorized();
        }

        if (!await ReauthenticateAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode,
                request.TwoFactorRecoveryCode))
        {
            return Unauthorized();
        }

        var provider =
            NormalizeSupportedProvider(
                request.Provider);

        if (provider is null)
        {
            return BadRequest(
                new
                {
                    error =
                        "ExternalProviderInvalid"
                });
        }

        var currentLogins =
            await _userManager.GetLoginsAsync(
                user);

        var login =
            currentLogins.FirstOrDefault(
                candidate =>
                    string.Equals(
                        candidate.LoginProvider,
                        provider,
                        StringComparison.OrdinalIgnoreCase));

        if (login is null)
        {
            /*
             * Treat an already-unlinked provider as an idempotent success.
             * This avoids leaking stale client state and makes retries safe.
             */
            return NoContent();
        }

        var removeResult =
            await _userManager.RemoveLoginAsync(
                user,
                login.LoginProvider,
                login.ProviderKey);

        if (!removeResult.Succeeded)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "External sign-in method could not be removed.");
        }

        return NoContent();
    }

    private async Task<ApplicationUser?> GetActiveUserAsync()
    {
        var user =
            await _userManager.GetUserAsync(
                User);

        if (user is null ||
            !user.IsActive ||
            await _userManager.IsLockedOutAsync(
                user))
        {
            return null;
        }

        return user;
    }

    private async Task<bool> ReauthenticateAsync(
        ApplicationUser user,
        string currentPassword,
        string? twoFactorCode,
        string? twoFactorRecoveryCode)
    {
        /*
         * Linking or unlinking a sign-in method changes a durable account
         * takeover boundary. A bearer session alone is never sufficient.
         * Password verification is always required, and when 2FA is enabled
         * the caller must additionally provide exactly one valid authenticator
         * code or one unused recovery code.
         */
        if (string.IsNullOrWhiteSpace(
                currentPassword) ||
            !await _userManager.CheckPasswordAsync(
                user,
                currentPassword))
        {
            return false;
        }

        var secondFactorResult =
            await _secondFactorVerifier.VerifyAsync(
                user,
                twoFactorCode,
                twoFactorRecoveryCode);

        return secondFactorResult is
            ExternalIdentitySecondFactorResult.NotRequired or
            ExternalIdentitySecondFactorResult.Succeeded;
    }

    private async Task<ExternalIdentity?>
        ValidateIdentityAsync(
            string provider,
            string idToken,
            CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                provider) ||
            string.IsNullOrWhiteSpace(
                idToken))
        {
            return null;
        }

        try
        {
            return await _tokenValidator.ValidateAsync(
                provider,
                idToken,
                cancellationToken);
        }
        catch (ExternalIdentityProviderUnavailableException)
        {
            /*
             * Provider discovery/key outages are intentionally collapsed to
             * a safe authentication failure. No upstream body, token, or
             * discovery detail is returned to the client.
             */
            return null;
        }
    }

    private static string? NormalizeSupportedProvider(
        string? provider)
    {
        if (string.IsNullOrWhiteSpace(
                provider))
        {
            return null;
        }

        return provider.Trim()
            .ToLowerInvariant() switch
        {
            ExternalIdentityProviders.Google =>
                ExternalIdentityProviders.Google,

            ExternalIdentityProviders.Apple =>
                ExternalIdentityProviders.Apple,

            ExternalIdentityProviders.Microsoft =>
                ExternalIdentityProviders.Microsoft,

            _ =>
                null
        };
    }

    private ObjectResult IdentityValidationProblem(
        IdentityResult result)
    {
        var errors =
            result.Errors
                .GroupBy(
                    error => error.Code,
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(
                            error =>
                                error.Description)
                        .ToArray(),
                    StringComparer.Ordinal);

        return new ObjectResult(
            new ValidationProblemDetails(
                errors))
        {
            StatusCode =
                StatusCodes.Status400BadRequest
        };
    }

    private static string GetProviderDisplayName(
        string provider)
    {
        return provider switch
        {
            ExternalIdentityProviders.Google =>
                "Google",

            ExternalIdentityProviders.Apple =>
                "Apple",

            ExternalIdentityProviders.Microsoft =>
                "Microsoft",

            _ =>
                "External provider"
        };
    }
}

public sealed record ExternalIdentityLoginRequest(
    string Provider,
    string IdToken,
    string? TwoFactorCode,
    string? TwoFactorRecoveryCode);

public sealed record ExternalIdentityRegisterRequest(
    string Provider,
    string IdToken,
    string Password,
    bool AcceptedTermsAndPrivacy,
    string LegalTermsVersion);

public sealed record ExternalIdentityLinkRequest(
    string Provider,
    string IdToken,
    string CurrentPassword,
    string? TwoFactorCode,
    string? TwoFactorRecoveryCode);

public sealed record ExternalIdentityUnlinkRequest(
    string Provider,
    string CurrentPassword,
    string? TwoFactorCode,
    string? TwoFactorRecoveryCode);
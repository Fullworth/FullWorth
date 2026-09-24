using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/account/security")]
[Authorize]
public sealed class AccountSecurityController(
    UserManager<ApplicationUser> userManager,
    IEmailSender<ApplicationUser> emailSender,
    IOptions<IdentityEmailOptions> emailOptions)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountSecurityResponse>> Get()
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        return Ok(await ToResponseAsync(user));
    }

    [HttpPost("profile")]
    public async Task<ActionResult<AccountSecurityResponse>> UpdateProfile(
        UpdateProfileRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var displayName =
            request.DisplayName?
                .Trim() ??
            string.Empty;

        if (displayName.Length > ApplicationUser.MaxDisplayNameLength)
        {
            return ValidationProblem(
                $"Display name must be {ApplicationUser.MaxDisplayNameLength} characters or fewer.");
        }

        var claims =
            await userManager.GetClaimsAsync(user);

        var existingDisplayNameClaims =
            claims
                .Where(
                    claim => string.Equals(
                        claim.Type,
                        ApplicationUser.DisplayNameClaimType,
                        StringComparison.Ordinal))
                .ToArray();

        if (existingDisplayNameClaims.Length > 0)
        {
            var removeResult =
                await userManager.RemoveClaimsAsync(
                    user,
                    existingDisplayNameClaims);

            if (!removeResult.Succeeded)
            {
                return IdentityValidationProblem(removeResult);
            }
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            var addResult =
                await userManager.AddClaimAsync(
                    user,
                    new Claim(
                        ApplicationUser.DisplayNameClaimType,
                        displayName));

            if (!addResult.Succeeded)
            {
                return IdentityValidationProblem(addResult);
            }
        }

        return Ok(await ToResponseAsync(user));
    }

    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(
        ChangePasswordRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return ValidationProblem(
                "Enter a new password.");
        }

        var result = await userManager.ChangePasswordAsync(
            user,
            request.CurrentPassword,
            request.NewPassword);

        if (!result.Succeeded)
        {
            return IdentityValidationProblem(result);
        }

        return NoContent();
    }

    [HttpPost("sessions/revoke-all")]
    public async Task<IActionResult> RevokeAllSessions(
        SensitiveCredentialRequest request)
    {
        var user =
            await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        /*
         * Rotating SecurityStamp invalidates every previously issued refresh
         * token for this Identity user. Existing bearer access tokens are
         * self-contained and remain valid only for their already-bounded
         * lifetime (currently 15 minutes); FullWorth does not claim that
         * remote access tokens disappear instantly.
         *
         * Stale refresh-family rows are harmless after the stamp rotation and
         * are pruned by the next legitimate family enrollment.
         */
        var revokeResult =
            await userManager.UpdateSecurityStampAsync(
                user);

        if (!revokeResult.Succeeded)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "FullWorth could not revoke account sessions safely.");
        }

        return NoContent();
    }

    [HttpPost("email")]
    public async Task<IActionResult> ChangeEmail(
        ChangeEmailRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        var newEmail = request.NewEmail.Trim();

        if (string.IsNullOrWhiteSpace(newEmail))
        {
            return ValidationProblem(
                "Enter a new email address.");
        }

        if (!emailOptions.Value.Enabled)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account email delivery is not configured yet.");
        }

        var existingUser =
            await userManager.FindByEmailAsync(newEmail);

        if (existingUser is not null &&
            existingUser.Id != user.Id)
        {
            return ValidationProblem(
                "That email address is already in use.");
        }

        if (string.Equals(
                user.Email,
                newEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            return ValidationProblem(
                "That is already your account email address.");
        }

        var token =
            await userManager.GenerateChangeEmailTokenAsync(
                user,
                newEmail);

        var confirmationLink = BuildConfirmationLink(
            user.Id,
            token,
            newEmail);

        await emailSender.SendConfirmationLinkAsync(
            user,
            newEmail,
            confirmationLink);

        return Ok(
            new
            {
                message =
                    "Check the new email address to confirm the change."
            });
    }

    [HttpPost("email/verification")]
    public async Task<IActionResult> ResendEmailVerification()
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        if (await userManager.IsEmailConfirmedAsync(user))
        {
            return NoContent();
        }

        if (!emailOptions.Value.Enabled)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Account email delivery is not configured yet.");
        }

        var email = user.Email?.Trim();

        if (string.IsNullOrWhiteSpace(email))
        {
            return ValidationProblem(
                "Your account does not have an email address to verify.");
        }

        var token =
            await userManager.GenerateEmailConfirmationTokenAsync(user);

        var confirmationLink = BuildConfirmationLink(
            user.Id,
            token);

        await emailSender.SendConfirmationLinkAsync(
            user,
            email,
            confirmationLink);

        return Ok(
            new
            {
                message =
                    "Check your email to verify your FullWorth account."
            });
    }

    [HttpPost("two-factor/setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> SetupTwoFactor(
        SensitiveCredentialRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        await userManager.SetTwoFactorEnabledAsync(
            user,
            false);

        var resetResult =
            await userManager.ResetAuthenticatorKeyAsync(user);

        if (!resetResult.Succeeded)
        {
            return IdentityValidationProblem(resetResult);
        }

        var sharedKey =
            await userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrWhiteSpace(sharedKey))
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "FullWorth could not create an authenticator key.");
        }

        var email = user.Email ?? string.Empty;

        return Ok(
            new TwoFactorSetupResponse(
                sharedKey,
                BuildOtpAuthUri(
                    email,
                    sharedKey)));
    }

    [HttpPost("two-factor/enable")]
    public async Task<ActionResult<TwoFactorRecoveryCodesResponse>> EnableTwoFactor(
        EnableTwoFactorRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        if (!await userManager.CheckPasswordAsync(
                user,
                request.CurrentPassword))
        {
            return UnauthorizedProblem(
                "Current password is incorrect.");
        }

        if (string.IsNullOrWhiteSpace(request.AuthenticatorCode) ||
            !await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                NormalizeAuthenticatorCode(request.AuthenticatorCode)))
        {
            return ValidationProblem(
                "The authenticator code is invalid.");
        }

        var enableResult =
            await userManager.SetTwoFactorEnabledAsync(
                user,
                true);

        if (!enableResult.Succeeded)
        {
            return IdentityValidationProblem(enableResult);
        }

        var recoveryCodes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user,
                10);

        return Ok(
            new TwoFactorRecoveryCodesResponse(
                recoveryCodes?.ToArray() ?? []));
    }

    [HttpPost("two-factor/recovery-codes")]
    public async Task<ActionResult<TwoFactorRecoveryCodesResponse>> RegenerateRecoveryCodes(
        SensitiveCredentialRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return ValidationProblem(
                "Two-factor authentication is not enabled.");
        }

        /*
         * Recovery codes are durable authentication credentials. Rotate the
         * security stamp on the tracked user before asking Identity to
         * replace them. GenerateNewTwoFactorRecoveryCodesAsync stages the
         * replacement and persists it through the same final user update, so
         * the new codes and refresh-session revocation commit together.
         */
        user.SecurityStamp =
            Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32))
                .ToLowerInvariant();

        var recoveryCodes =
            await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
                user,
                10);

        if (recoveryCodes is null)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status503ServiceUnavailable,
                title:
                    "FullWorth could not regenerate recovery codes safely.");
        }

        return Ok(
            new TwoFactorRecoveryCodesResponse(
                recoveryCodes.ToArray()));
    }

    [HttpPost("two-factor/disable")]
    public async Task<IActionResult> DisableTwoFactor(
        SensitiveCredentialRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        var result =
            await userManager.SetTwoFactorEnabledAsync(
                user,
                false);

        if (!result.Succeeded)
        {
            return IdentityValidationProblem(result);
        }

        return NoContent();
    }

    [HttpPost("two-factor/reset")]
    public async Task<ActionResult<TwoFactorSetupResponse>> ResetTwoFactor(
        SensitiveCredentialRequest request)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                user,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        await userManager.SetTwoFactorEnabledAsync(
            user,
            false);

        var resetResult =
            await userManager.ResetAuthenticatorKeyAsync(user);

        if (!resetResult.Succeeded)
        {
            return IdentityValidationProblem(resetResult);
        }

        var sharedKey =
            await userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrWhiteSpace(sharedKey))
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "FullWorth could not reset the authenticator key.");
        }

        return Ok(
            new TwoFactorSetupResponse(
                sharedKey,
                BuildOtpAuthUri(
                    user.Email ?? string.Empty,
                    sharedKey)));
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userId = userManager.GetUserId(User);

        return Guid.TryParse(userId, out var parsedUserId)
            ? await userManager.FindByIdAsync(
                parsedUserId.ToString())
            : null;
    }

    private async Task<ActionResult?> ValidateSensitiveCredentialsAsync(
        ApplicationUser user,
        string currentPassword,
        string? twoFactorCode)
    {
        if (string.IsNullOrWhiteSpace(currentPassword) ||
            !await userManager.CheckPasswordAsync(
                user,
                currentPassword))
        {
            return UnauthorizedProblem(
                "Current password is incorrect.");
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(twoFactorCode))
        {
            return UnauthorizedProblem(
                "A current authenticator code is required.");
        }

        var isValidCode =
            await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                NormalizeAuthenticatorCode(twoFactorCode));

        return isValidCode
            ? null
            : UnauthorizedProblem(
                "The authenticator code is invalid.");
    }

    private async Task<AccountSecurityResponse> ToResponseAsync(
        ApplicationUser user)
    {
        var authenticatorKey =
            await userManager.GetAuthenticatorKeyAsync(user);

        var claims =
            await userManager.GetClaimsAsync(user);

        var displayName =
            claims
                .FirstOrDefault(
                    claim => string.Equals(
                        claim.Type,
                        ApplicationUser.DisplayNameClaimType,
                        StringComparison.Ordinal))?
                .Value ??
            string.Empty;

        return new AccountSecurityResponse(
            displayName,
            user.Email ?? string.Empty,
            await userManager.IsEmailConfirmedAsync(user),
            await userManager.GetTwoFactorEnabledAsync(user),
            !string.IsNullOrWhiteSpace(authenticatorKey),
            await userManager.CountRecoveryCodesAsync(user));
    }

    private string BuildConfirmationLink(
        Guid userId,
        string token,
        string? changedEmail = null)
    {
        var encodedCode =
            WebEncoders.Base64UrlEncode(
                Encoding.UTF8.GetBytes(token));

        var publicBase =
            emailOptions.Value.PublicWebBaseUrl
                .TrimEnd('/');

        var confirmationLink =
            $"{publicBase}/auth/confirm-email?userId={Uri.EscapeDataString(userId.ToString())}&code={Uri.EscapeDataString(encodedCode)}";

        return string.IsNullOrWhiteSpace(changedEmail)
            ? confirmationLink
            : $"{confirmationLink}&changedEmail={Uri.EscapeDataString(changedEmail)}";
    }

    private ObjectResult UnauthorizedProblem(
        string title)
    {
        return Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: title);
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
                        .Select(error => error.Description)
                        .ToArray(),
                    StringComparer.Ordinal);

        return new ObjectResult(
            new ValidationProblemDetails(errors))
        {
            StatusCode =
                StatusCodes.Status400BadRequest
        };
    }

    private static string NormalizeAuthenticatorCode(
        string code)
    {
        return code
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);
    }

    private static string BuildOtpAuthUri(
        string email,
        string sharedKey)
    {
        var label =
            Uri.EscapeDataString(
                $"FullWorth:{email}");

        return
            $"otpauth://totp/{label}?secret={Uri.EscapeDataString(sharedKey)}&issuer=FullWorth&digits=6";
    }
}

public sealed record AccountSecurityResponse(
    string DisplayName,
    string Email,
    bool EmailConfirmed,
    bool TwoFactorEnabled,
    bool HasAuthenticatorKey,
    int RecoveryCodesLeft);

public sealed record UpdateProfileRequest(
    string? DisplayName);

public sealed record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword,
    string? TwoFactorCode);

public sealed record ChangeEmailRequest(
    string CurrentPassword,
    string NewEmail,
    string? TwoFactorCode);

public sealed record SensitiveCredentialRequest(
    string CurrentPassword,
    string? TwoFactorCode);

public sealed record EnableTwoFactorRequest(
    string CurrentPassword,
    string AuthenticatorCode);

public sealed record TwoFactorSetupResponse(
    string SharedKey,
    string OtpAuthUri);

public sealed record TwoFactorRecoveryCodesResponse(
    IReadOnlyList<string> RecoveryCodes);

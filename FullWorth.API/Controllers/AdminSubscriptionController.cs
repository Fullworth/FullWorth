using FullWorth.API.Authorization;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Subscriptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/admin/subscription")]
[Authorize(Policy = FullWorthPolicies.AdminOrOwner)]
public sealed class AdminSubscriptionController(
    UserManager<ApplicationUser> userManager,
    AdminSubscriptionAccessKeyService accessKeyService)
    : ControllerBase
{
    [HttpPost("access-keys")]
    public async Task<ActionResult<CreatedAccessKeyResponse>> CreateAccessKey(
        CreateAccessKeyRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        var actor =
            await userManager.FindByIdAsync(
                actorUserId.ToString());

        if (actor is null)
        {
            return Unauthorized();
        }

        var credentialError =
            await ValidateSensitiveCredentialsAsync(
                actor,
                request.CurrentPassword,
                request.TwoFactorCode);

        if (credentialError is not null)
        {
            return credentialError;
        }

        if (!Enum.TryParse<SubscriptionAccessKeyPurpose>(
                request.Purpose,
                ignoreCase: true,
                out var purpose) ||
            !Enum.IsDefined(purpose) ||
            !Enum.TryParse<FullWorthSubscriptionTier>(
                request.Tier,
                ignoreCase: true,
                out var tier) ||
            !Enum.IsDefined(tier))
        {
            return ValidationProblem(
                "Purpose or tier is invalid.");
        }

        try
        {
            var created = await accessKeyService.CreateAsync(
                actorUserId,
                purpose,
                tier,
                request.DurationDays,
                request.GrantsLifetimeAccess,
                request.MaxRedemptions,
                request.ExpiresAtUtc,
                cancellationToken,
                request.Label);

            return StatusCode(
                StatusCodes.Status201Created,
                new CreatedAccessKeyResponse(
                    created.Id,
                    created.PlaintextKey,
                    created.DisplayPrefix,
                    created.Label,
                    created.Purpose.ToString(),
                    created.Tier.ToString(),
                    created.DurationDays,
                    created.GrantsLifetimeAccess,
                    created.MaxRedemptions,
                    created.ExpiresAtUtc,
                    created.CreatedAtUtc));
        }
        catch (ArgumentException exception)
        {
            return ValidationProblem(exception.Message);
        }
    }

    [HttpPost("access-keys/{accessKeyId:guid}/revoke")]
    public async Task<IActionResult> RevokeAccessKey(
        Guid accessKeyId,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorUserId(out var actorUserId))
        {
            return Unauthorized();
        }

        return await accessKeyService.RevokeAsync(
            actorUserId,
            accessKeyId,
            cancellationToken)
                ? NoContent()
                : NotFound();
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
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title:
                    "Current password is incorrect.");
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(twoFactorCode))
        {
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title:
                    "A current authenticator code is required.");
        }

        var validTwoFactorCode =
            await userManager.VerifyTwoFactorTokenAsync(
                user,
                userManager.Options.Tokens.AuthenticatorTokenProvider,
                NormalizeAuthenticatorCode(twoFactorCode));

        if (!validTwoFactorCode)
        {
            return Problem(
                statusCode:
                    StatusCodes.Status401Unauthorized,
                title:
                    "The authenticator code is invalid.");
        }

        return null;
    }

    private bool TryGetActorUserId(out Guid actorUserId)
    {
        return Guid.TryParse(
            userManager.GetUserId(User),
            out actorUserId);
    }

    private static string NormalizeAuthenticatorCode(
        string code)
    {
        return code
            .Replace(
                " ",
                string.Empty,
                StringComparison.Ordinal)
            .Replace(
                "-",
                string.Empty,
                StringComparison.Ordinal);
    }
}

public sealed record CreateAccessKeyRequest(
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    DateTimeOffset? ExpiresAtUtc,
    string CurrentPassword,
    string? TwoFactorCode,
    string? Label = null);

public sealed record CreatedAccessKeyResponse(
    Guid Id,
    string PlaintextKey,
    string DisplayPrefix,
    string? Label,
    string Purpose,
    string Tier,
    int? DurationDays,
    bool GrantsLifetimeAccess,
    int MaxRedemptions,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc);

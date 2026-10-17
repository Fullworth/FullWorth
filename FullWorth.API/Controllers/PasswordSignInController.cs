using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using FullWorth.API.Authorization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullWorth.API.Controllers;

[ApiController]
[AllowAnonymous]
[SubscriptionAccessExempt]
[Route("api/auth/password")]
[EnableRateLimiting("authentication")]
public sealed class PasswordSignInController(
    FullWorthDbContext database,
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    IPasskeyHandler<ApplicationUser> passkeys,
    PendingPasswordLoginStore pending,
    PasskeyCeremonyStore ceremonies,
    IOptions<FullWorthPasskeyOptions> configuration) : ControllerBase
{
    private IActionResult Denied() => Unauthorized(new { title = "Sign-in verification failed. Start again." });

    [HttpPost("start")]
    public async Task<IActionResult> Start(PasswordSignInRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null || !user.IsActive ||
            !(await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            return Denied();

        if (!await users.GetTwoFactorEnabledAsync(user))
            return await IssueTokens(user, await users.GetSecurityStampAsync(user), false);
        var issued = pending.Create(user.Id, await users.GetSecurityStampAsync(user));
        if (issued is null) return StatusCode(503);
        return Accepted(new { issued.LoginId, issued.ExpiresAt,
            canUsePasskey = await CanUsePasskey(user) });
    }

    [HttpPost("passkey-options")]
    public async Task<IActionResult> PasskeyOptions(PendingPasswordRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var proof = pending.Read(request.LoginId);
        var user = await CurrentUser(proof);
        if (user is null || !await CanUsePasskey(user)) return Denied();
        var options = await passkeys.MakeRequestOptionsAsync(null, HttpContext);
        var id = ceremonies.Create("password:" + request.LoginId, options.AssertionState,
            user.Id, proof!.SecurityStamp);
        return id is null ? StatusCode(503) : Ok(new { ceremonyId = id,
            options = JsonSerializer.Deserialize<JsonElement>(options.RequestOptionsJson) });
    }

    [HttpPost("complete")]
    public async Task<IActionResult> Complete(PasswordVerificationRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        // Every verification attempt consumes its proof atomically. Invalid codes
        // get a new ID, with the original expiry and a strictly decreasing budget.
        var proof = pending.Consume(request.LoginId);
        var user = await CurrentUser(proof);
        if (user is null) return Denied();

        var hasCode = !string.IsNullOrWhiteSpace(request.TwoFactorCode);
        var hasRecovery = !string.IsNullOrWhiteSpace(request.RecoveryCode);
        var hasPasskey = !string.IsNullOrWhiteSpace(request.CredentialJson) ||
            !string.IsNullOrWhiteSpace(request.CeremonyId);
        var methodCount = (hasCode ? 1 : 0) + (hasRecovery ? 1 : 0) + (hasPasskey ? 1 : 0);
        var valid = false;

        if (methodCount == 1 && hasPasskey && configuration.Value.Enabled)
        {
            var ceremony = ceremonies.Consume(request.CeremonyId, "password:" + request.LoginId, user.Id);
            if (ceremony?.SecurityStamp == proof!.SecurityStamp && !string.IsNullOrWhiteSpace(request.CredentialJson))
            {
                // Same lock as registration/removal/standalone assertion. A key
                // removed during verification must never be resurrected by upsert.
                await using var transaction = database.Database.IsRelational()
                    ? await database.Database.BeginTransactionAsync(HttpContext.RequestAborted) : null;
                if (transaction is not null)
                    await database.Database.ExecuteSqlRawAsync(
                        "SELECT pg_advisory_xact_lock(74128596321048)", HttpContext.RequestAborted);
                var assertion = await passkeys.PerformAssertionAsync(new()
                {
                    HttpContext = HttpContext, CredentialJson = request.CredentialJson!,
                    AssertionState = ceremony.State
                });
                valid = assertion.Succeeded && assertion.User.Id == user.Id &&
                    (await users.AddOrUpdatePasskeyAsync(user, assertion.Passkey)).Succeeded;
                if (valid && transaction is not null)
                    await transaction.CommitAsync(HttpContext.RequestAborted);
            }
        }
        else if (methodCount == 1 && hasCode)
        {
            valid = await users.VerifyTwoFactorTokenAsync(user,
                users.Options.Tokens.AuthenticatorTokenProvider, Normalize(request.TwoFactorCode!));
        }
        else if (methodCount == 1 && hasRecovery)
        {
            valid = (await users.RedeemTwoFactorRecoveryCodeAsync(user, request.RecoveryCode!.Trim())).Succeeded;
        }

        if (!valid)
        {
            if (methodCount == 1 && (hasCode || hasRecovery))
                await users.AccessFailedAsync(user);
            if (await users.IsLockedOutAsync(user)) return Denied();
            var retry = pending.Retry(proof!);
            return retry is null ? Denied() : Unauthorized(new { retry.LoginId, retry.ExpiresAt,
                canUsePasskey = await CanUsePasskey(user), title = "Verification failed. Try another method or code." });
        }

        if (!(await users.ResetAccessFailedCountAsync(user)).Succeeded) return Denied();
        return await IssueTokens(user, proof!.SecurityStamp, true);
    }

    private async Task<ApplicationUser?> CurrentUser(PendingPasswordLoginStore.Proof? proof)
    {
        if (proof is null) return null;
        var user = await users.FindByIdAsync(proof.UserId.ToString());
        return user is not null && user.IsActive &&
            proof.SecurityStamp == await users.GetSecurityStampAsync(user) &&
            await signIn.CanSignInAsync(user) && !await users.IsLockedOutAsync(user) &&
            await users.GetTwoFactorEnabledAsync(user) ? user : null;
    }

    private async Task<bool> CanUsePasskey(ApplicationUser user) =>
        configuration.Value.Enabled && (await users.GetPasskeysAsync(user)).Count > 0;

    private async Task<IActionResult> IssueTokens(ApplicationUser user, string expectedStamp, bool expectedTwoFactor)
    {
        await database.Entry(user).ReloadAsync(HttpContext.RequestAborted);
        if (await users.GetSecurityStampAsync(user) != expectedStamp ||
            await users.GetTwoFactorEnabledAsync(user) != expectedTwoFactor || !user.IsActive || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            return Denied();
        var principal = await signIn.CreateUserPrincipalAsync(user);
        return SignIn(principal, IdentityConstants.BearerScheme);
    }

    private static string Normalize(string code) => code.Replace(" ", "").Replace("-", "");
}

public sealed record PasswordSignInRequest(
    [Required, EmailAddress, MaxLength(256)] string Email,
    [Required, MaxLength(1024)] string Password);
public sealed record PendingPasswordRequest(
    [Required, StringLength(43, MinimumLength = 43)] string LoginId);
public sealed record PasswordVerificationRequest(
    [Required, StringLength(43, MinimumLength = 43)] string LoginId,
    [MaxLength(12)] string? TwoFactorCode = null,
    [MaxLength(64)] string? RecoveryCode = null,
    [StringLength(43, MinimumLength = 43)] string? CeremonyId = null,
    [MaxLength(32768)] string? CredentialJson = null);

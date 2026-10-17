using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FullWorth.Web.Services;

public sealed partial class WebAuthenticationService
{
    public async Task<PasswordStepResult> BeginPasswordSignInAsync(HttpContext context,
        string email, string password, bool rememberMe, CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            return new(new(false, "Sign out before signing in to another account."), null);
        var client = _httpClientFactory.CreateClient("FullWorthApi");
        using var response = await client.PostAsJsonAsync("/api/auth/password/start",
            new { email = email.Trim(), password }, cancellationToken);
        return await ReadPasswordStep(context, response, email.Trim(), rememberMe, cancellationToken);
    }

    public async Task<PasswordStepResult> CompletePasswordSignInAsync(HttpContext context,
        PendingWebPasswordLogin pending, string? code, string? recovery,
        string? ceremonyId, string? credentialJson, CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated == true)
            return new(new(false, "Sign out before signing in to another account."), null);
        var client = _httpClientFactory.CreateClient("FullWorthApi");
        using var response = await client.PostAsJsonAsync("/api/auth/password/complete",
            new { pending.LoginId, twoFactorCode = NormalizeOptionalCode(code),
                recoveryCode = NormalizeOptionalCode(recovery), ceremonyId, credentialJson }, cancellationToken);
        return await ReadPasswordStep(context, response, pending.Email, pending.RememberMe, cancellationToken);
    }

    private async Task<PasswordStepResult> ReadPasswordStep(HttpContext context, HttpResponseMessage response,
        string email, bool rememberMe, CancellationToken cancellationToken)
    {
        if (response.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.Unauthorized)
        {
            try
            {
                var pending = await response.Content.ReadFromJsonAsync<PendingStepResponse>(cancellationToken);
                if (pending?.LoginId?.Length == 43 && pending.ExpiresAt > DateTimeOffset.UtcNow &&
                    pending.ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(4))
                    return new(new(false, response.StatusCode == HttpStatusCode.Unauthorized
                            ? "Verification failed. Try another method or code." : null, true),
                        new(pending.LoginId, pending.ExpiresAt, email, rememberMe, pending.CanUsePasskey));
            }
            catch (JsonException) { }
        }
        if (!response.IsSuccessStatusCode)
            return new(new(false, GetSafeLoginError(response.StatusCode)), null);
        var tokens = await ReadAccessTokenResponseAsync(response, cancellationToken);
        if (tokens is not null) tokens = await EnrollRefreshFamilyAsync(tokens, cancellationToken);
        if (tokens is null) return new(new(false, "FullWorth could not establish a secure sign-in session."), null);
        await SignInWebSessionAsync(context, email, email, email, tokens, rememberMe);
        return new(AuthOperationResult.Success, null);
    }

    private sealed record PendingStepResponse(string? LoginId, DateTimeOffset ExpiresAt, bool CanUsePasskey);
}

public sealed record PasswordStepResult(AuthOperationResult Result, PendingWebPasswordLogin? Pending);

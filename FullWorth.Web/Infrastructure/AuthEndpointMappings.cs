using FullWorth.Core.Legal;
using FullWorth.Web.Components;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace FullWorth.Web.Infrastructure;

public static class AuthEndpointMappings
{
    public const string AuthenticationRateLimitPolicy =
        "web-authentication";

    public static IEndpointRouteBuilder
        MapFullWorthAuthEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        endpoints.MapPost(
            "/auth/login",
            async (HttpContext context, IAntiforgery antiforgery,
                WebAuthenticationService authenticationService, PendingPasswordLoginCookie pendingCookie,
                IStringLocalizer<App> localizer) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                context.Response.Headers.CacheControl = "no-store";
                var form = await context.Request.ReadFormAsync(context.RequestAborted);
                var completing = string.Equals(form["twoFactor"], "true", StringComparison.OrdinalIgnoreCase);
                PasswordStepResult step;
                if (completing)
                {
                    var pending = pendingCookie.Read(context);
                    pendingCookie.Clear(context);
                    if (pending is null)
                        return Results.Redirect("/login?error=" + Uri.EscapeDataString(
                            localizer["Your sign-in expired. Enter your email and password again."]));
                    step = await authenticationService.CompletePasswordSignInAsync(context, pending,
                        form["twoFactorCode"].ToString(), form["recoveryCode"].ToString(),
                        null, null, context.RequestAborted);
                }
                else
                {
                    pendingCookie.Clear(context);
                    var email = form["email"].ToString().Trim();
                    var password = form["password"].ToString();
                    if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                        return Results.Redirect(BuildLoginRedirect(localizer["Enter your email and password."], false));
                    step = await authenticationService.BeginPasswordSignInAsync(context, email, password,
                        string.Equals(form["rememberMe"], "on", StringComparison.OrdinalIgnoreCase),
                        context.RequestAborted);
                }
                if (step.Pending is not null)
                {
                    pendingCookie.Write(context, step.Pending);
                    var target = "/login?twoFactor=true" + (step.Pending.CanUsePasskey ? "&passkey=true" : "");
                    if (!string.IsNullOrWhiteSpace(step.Result.ErrorMessage))
                        target += "&error=" + Uri.EscapeDataString(localizer[step.Result.ErrorMessage]);
                    return Results.Redirect(target);
                }
                return step.Result.Succeeded ? Results.Redirect("/app")
                    : Results.Redirect(BuildLoginRedirect(
                        LocalizeResultMessage(localizer, step.Result.ErrorMessage, "Unable to sign in."), false));
            }).RequireRateLimiting(AuthenticationRateLimitPolicy);

        endpoints.MapPost("/auth/login/passkey-options",
            async (HttpContext context, IAntiforgery antiforgery, PendingPasswordLoginCookie pendingCookie,
                IHttpClientFactory clients, IConfiguration configuration) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                context.Response.Headers.CacheControl = "no-store";
                var pending = pendingCookie.Read(context);
                if (context.User.Identity?.IsAuthenticated == true || pending?.CanUsePasskey != true ||
                    !configuration.GetValue<bool>("Passkeys:Enabled")) return Results.Unauthorized();
                using var response = await clients.CreateClient("FullWorthApi").PostAsJsonAsync(
                    "/api/auth/password/passkey-options", new { pending.LoginId }, context.RequestAborted);
                return response.IsSuccessStatusCode
                    ? Results.Content(await response.Content.ReadAsStringAsync(context.RequestAborted), "application/json")
                    : Results.StatusCode((int)response.StatusCode);
            }).RequireRateLimiting(AuthenticationRateLimitPolicy);

        endpoints.MapPost("/auth/login/passkey-complete",
            async (HttpContext context, IAntiforgery antiforgery, PendingPasswordLoginCookie pendingCookie,
                WebAuthenticationService authenticationService, IConfiguration configuration,
                PasswordPasskeyRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);
                context.Response.Headers.CacheControl = "no-store";
                var pending = pendingCookie.Read(context);
                if (context.User.Identity?.IsAuthenticated == true || pending?.CanUsePasskey != true ||
                    !configuration.GetValue<bool>("Passkeys:Enabled")) return Results.Unauthorized();
                pendingCookie.Clear(context);
                if (request.CeremonyId?.Length != 43 || string.IsNullOrWhiteSpace(request.CredentialJson) ||
                    request.CredentialJson.Length > 32768) return Results.BadRequest();
                var step = await authenticationService.CompletePasswordSignInAsync(context, pending,
                    null, null, request.CeremonyId, request.CredentialJson, context.RequestAborted);
                if (step.Pending is not null) pendingCookie.Write(context, step.Pending);
                return step.Result.Succeeded ? Results.NoContent() : Results.Unauthorized();
            }).RequireRateLimiting(AuthenticationRateLimitPolicy);

        endpoints.MapPost(
            "/auth/register",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService,
                IStringLocalizer<App> localizer) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                var form =
                    await context.Request
                        .ReadFormAsync(
                            context.RequestAborted);

                var email =
                    form["email"]
                        .ToString()
                        .Trim();

                var password =
                    form["password"]
                        .ToString();

                var confirmPassword =
                    form["confirmPassword"]
                        .ToString();

                var acceptedTermsAndPrivacy =
                    string.Equals(
                        form["acceptedTermsAndPrivacy"],
                        "on",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        form["acceptedTermsAndPrivacy"],
                        "true",
                        StringComparison.OrdinalIgnoreCase);

                var legalTermsVersion =
                    form["legalTermsVersion"]
                        .ToString()
                        .Trim();

                if (string.IsNullOrWhiteSpace(
                        email))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "Enter an email address."]));
                }

                if (string.IsNullOrWhiteSpace(
                        password))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "Create a password."]));
                }

                if (!string.Equals(
                        password,
                        confirmPassword,
                        StringComparison.Ordinal))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "The passwords do not match."]));
                }

                if (!acceptedTermsAndPrivacy ||
                    !string.Equals(
                        legalTermsVersion,
                        FullWorthLegalDocuments.CurrentVersion,
                        StringComparison.Ordinal))
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "Accept the current FullWorth Terms and Privacy Notice to create an account."]));
                }

                var result =
                    await authenticationService
                        .RegisterAsync(
                            context,
                            email,
                            password,
                            acceptedTermsAndPrivacy,
                            legalTermsVersion,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/register?error=" +
                        Uri.EscapeDataString(
                            LocalizeResultMessage(
                                localizer,
                                result.ErrorMessage,
                                "Unable to create your account.")));
                }

                return Results.Redirect(
                    "/login?message=" +
                    Uri.EscapeDataString(
                        localizer[
                            "Registration request received. Sign in to continue."]));
            }).RequireRateLimiting(
                AuthenticationRateLimitPolicy);

        endpoints.MapPost(
            "/auth/forgot-password",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService,
                IStringLocalizer<App> localizer) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                var form =
                    await context.Request
                        .ReadFormAsync(
                            context.RequestAborted);

                var email =
                    form["email"]
                        .ToString()
                        .Trim();

                if (string.IsNullOrWhiteSpace(
                        email))
                {
                    return Results.Redirect(
                        "/forgot-password?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "Enter your email address."]));
                }

                var result =
                    await authenticationService
                        .RequestPasswordResetAsync(
                            email,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/forgot-password?error=" +
                        Uri.EscapeDataString(
                            LocalizeResultMessage(
                                localizer,
                                result.ErrorMessage,
                                "Password recovery is unavailable right now.")));
                }

                return Results.Redirect(
                    "/forgot-password?sent=true");
            }).RequireRateLimiting(
                AuthenticationRateLimitPolicy);

        endpoints.MapPost(
            "/auth/reset-password",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService,
                IStringLocalizer<App> localizer) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                var form =
                    await context.Request
                        .ReadFormAsync(
                            context.RequestAborted);

                var email =
                    form["email"]
                        .ToString()
                        .Trim();

                var code =
                    form["code"]
                        .ToString()
                        .Trim();

                var newPassword =
                    form["newPassword"]
                        .ToString();

                var confirmPassword =
                    form["confirmPassword"]
                        .ToString();

                var returnQuery =
                    BuildResetPasswordQuery(
                        email,
                        code);

                if (string.IsNullOrWhiteSpace(
                        email) ||
                    string.IsNullOrWhiteSpace(
                        code))
                {
                    return Results.Redirect(
                        "/reset-password?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "This password reset link is incomplete or invalid."]));
                }

                if (string.IsNullOrWhiteSpace(
                        newPassword))
                {
                    return Results.Redirect(
                        "/reset-password" +
                        returnQuery +
                        "&error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "Create a new password."]));
                }

                if (!string.Equals(
                        newPassword,
                        confirmPassword,
                        StringComparison.Ordinal))
                {
                    return Results.Redirect(
                        "/reset-password" +
                        returnQuery +
                        "&error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "The passwords do not match."]));
                }

                var result =
                    await authenticationService
                        .ResetPasswordAsync(
                            email,
                            code,
                            newPassword,
                            context.RequestAborted);

                if (!result.Succeeded)
                {
                    return Results.Redirect(
                        "/reset-password" +
                        returnQuery +
                        "&error=" +
                        Uri.EscapeDataString(
                            LocalizeResultMessage(
                                localizer,
                                result.ErrorMessage,
                                "Unable to reset your password.")));
                }

                return Results.Redirect(
                    "/login?message=" +
                    Uri.EscapeDataString(
                        localizer[
                            "Your password has been reset. Sign in with your new password."]));
            }).RequireRateLimiting(
                AuthenticationRateLimitPolicy);

        endpoints.MapGet(
            "/auth/confirm-email",
            async (
                HttpContext context,
                WebAuthenticationService authenticationService,
                IStringLocalizer<App> localizer) =>
            {
                var userId =
                    context.Request.Query["userId"]
                        .ToString();

                var code =
                    context.Request.Query["code"]
                        .ToString();

                var changedEmail =
                    context.Request.Query["changedEmail"]
                        .ToString();

                if (string.IsNullOrWhiteSpace(
                        userId) ||
                    string.IsNullOrWhiteSpace(
                        code))
                {
                    return Results.Redirect(
                        "/login?error=" +
                        Uri.EscapeDataString(
                            localizer[
                                "This email confirmation link is incomplete or invalid."]));
                }

                var result =
                    await authenticationService
                        .ConfirmEmailAsync(
                            userId,
                            code,
                            changedEmail,
                            context.RequestAborted);

                if (result.Succeeded &&
                    !string.IsNullOrWhiteSpace(
                        changedEmail))
                {
                    await authenticationService
                        .LogoutAsync(
                            context);
                }

                return Results.Redirect(
                    result.Succeeded
                        ? "/login?message=" +
                          Uri.EscapeDataString(
                              localizer[
                                  string.IsNullOrWhiteSpace(changedEmail)
                                      ? "Your email address is confirmed."
                                      : "Your email address was changed. Sign in with the new address."])
                        : "/login?error=" +
                          Uri.EscapeDataString(
                              LocalizeResultMessage(
                                  localizer,
                                  result.ErrorMessage,
                                  "Unable to confirm this email address.")));
            });

        endpoints.MapPost(
            "/auth/logout",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                WebAuthenticationService authenticationService) =>
            {
                await antiforgery
                    .ValidateRequestAsync(
                        context);

                await authenticationService
                    .LogoutAsync(
                        context,
                        context.RequestAborted);

                return Results.Redirect(
                    "/");
            });

        return endpoints;
    }

    private static string LocalizeResultMessage(
        IStringLocalizer<App> localizer,
        string? message,
        string fallback)
    {
        var key =
            string.IsNullOrWhiteSpace(message)
                ? fallback
                : message;

        return localizer[key].Value;
    }

    private static string BuildLoginRedirect(
        string error,
        bool twoFactor)
    {
        var path =
            twoFactor
                ? "/login?twoFactor=true&error="
                : "/login?error=";

        return path +
            Uri.EscapeDataString(
                error);
    }

    private static string BuildResetPasswordQuery(
        string email,
        string code)
    {
        return
            $"?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}";
    }
}

public sealed record PasswordPasskeyRequest(string CeremonyId, string CredentialJson);

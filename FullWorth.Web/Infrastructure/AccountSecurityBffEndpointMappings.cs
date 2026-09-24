using FullWorth.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace FullWorth.Web.Infrastructure;

public static class AccountSecurityBffEndpointMappings
{
    public static IEndpointRouteBuilder MapFullWorthAccountSecurityBffEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var bff = endpoints.MapGroup("/bff/account/security")
            .RequireAuthorization();

        bff.MapGet(
            string.Empty,
            async (
                HttpContext context,
                FullWorthBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/account/security",
                    context.RequestAborted));

        MapSecurePost<UpdateProfileBffRequest>(
            bff,
            "/profile",
            "/api/account/security/profile");

        bff.MapPost(
            "/password",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                ChangePasswordBffRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(
                    context);

                return await writeProxyService
                    .ForwardJsonAndSignOutOnSuccessAsync(
                        context,
                        HttpMethod.Post,
                        "/api/account/security/password",
                        request,
                        context.RequestAborted);
            });

        bff.MapPost(
            "/sessions/revoke-all",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                SensitiveCredentialBffRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(
                    context);

                return await writeProxyService
                    .ForwardJsonAndSignOutOnSuccessAsync(
                        context,
                        HttpMethod.Post,
                        "/api/account/security/sessions/revoke-all",
                        request,
                        context.RequestAborted);
            });

        MapSecurePost<ChangeEmailBffRequest>(
            bff,
            "/email",
            "/api/account/security/email");

        MapSecurePost<ResendEmailVerificationBffRequest>(
            bff,
            "/email/verification",
            "/api/account/security/email/verification");

        MapSecurePost<SensitiveCredentialBffRequest>(
            bff,
            "/two-factor/setup",
            "/api/account/security/two-factor/setup");

        MapSecurePost<EnableTwoFactorBffRequest>(
            bff,
            "/two-factor/enable",
            "/api/account/security/two-factor/enable");

        MapSecurePost<SensitiveCredentialBffRequest>(
            bff,
            "/two-factor/recovery-codes",
            "/api/account/security/two-factor/recovery-codes");

        MapSecurePost<SensitiveCredentialBffRequest>(
            bff,
            "/two-factor/disable",
            "/api/account/security/two-factor/disable");

        MapSecurePost<SensitiveCredentialBffRequest>(
            bff,
            "/two-factor/reset",
            "/api/account/security/two-factor/reset");

        return endpoints;
    }

    private static void MapSecurePost<TRequest>(
        RouteGroupBuilder group,
        string bffPath,
        string apiPath)
        where TRequest : class
    {
        group.MapPost(
            bffPath,
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                TRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Post,
                    apiPath,
                    request,
                    context.RequestAborted);
            });
    }
}

public sealed record UpdateProfileBffRequest(
    string? DisplayName);

public sealed record ChangePasswordBffRequest(
    string CurrentPassword,
    string NewPassword,
    string? TwoFactorCode);

public sealed record ChangeEmailBffRequest(
    string CurrentPassword,
    string NewEmail,
    string? TwoFactorCode);

public sealed record ResendEmailVerificationBffRequest();

public sealed record SensitiveCredentialBffRequest(
    string CurrentPassword,
    string? TwoFactorCode);

public sealed record EnableTwoFactorBffRequest(
    string CurrentPassword,
    string AuthenticatorCode);

using FullWorth.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace FullWorth.Web.Infrastructure;

public static class ExternalIdentityManagementEndpointMappings
{
    public static IEndpointRouteBuilder MapFullWorthExternalIdentityManagementEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
                "/bff/account/external",
                async (
                    HttpContext context,
                    FullWorthBffProxyService proxyService) =>
                    await proxyService.ForwardGetAsync(
                        context,
                        "/api/auth/external",
                        context.RequestAborted))
            .RequireAuthorization();

        endpoints.MapPost(
                "/bff/account/external/unlink",
                async (
                    HttpContext context,
                    IAntiforgery antiforgery,
                    AdminBffWriteProxyService writeProxyService,
                    ExternalIdentityUnlinkBffRequest request) =>
                {
                    await antiforgery.ValidateRequestAsync(
                        context);

                    return await writeProxyService.ForwardJsonAsync(
                        context,
                        HttpMethod.Post,
                        "/api/auth/external/unlink",
                        request,
                        context.RequestAborted);
                })
            .RequireAuthorization();

        return endpoints;
    }
}

public sealed record ExternalIdentityUnlinkBffRequest(
    string Provider,
    string CurrentPassword,
    string? TwoFactorCode,
    string? TwoFactorRecoveryCode);

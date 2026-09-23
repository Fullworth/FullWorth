using FullWorth.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace FullWorth.Web.Infrastructure;

public static class AccountPreferenceBffEndpointMappings
{
    public static IEndpointRouteBuilder MapFullWorthAccountPreferenceBffEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var bff = endpoints.MapGroup("/bff/account/preferences")
            .RequireAuthorization();

        bff.MapGet(
            string.Empty,
            async (
                HttpContext context,
                FullWorthBffProxyService proxyService) =>
                await proxyService.ForwardGetAsync(
                    context,
                    "/api/account/preferences",
                    context.RequestAborted));

        bff.MapPut(
            string.Empty,
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                AccountPreferenceUpdateRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Put,
                    "/api/account/preferences",
                    request,
                    context.RequestAborted);
            });

        bff.MapPut(
            "/experience",
            async (
                HttpContext context,
                IAntiforgery antiforgery,
                AdminBffWriteProxyService writeProxyService,
                ExperiencePreferenceUpdateRequest request) =>
            {
                await antiforgery.ValidateRequestAsync(context);

                return await writeProxyService.ForwardJsonAsync(
                    context,
                    HttpMethod.Put,
                    "/api/account/preferences/experience",
                    request,
                    context.RequestAborted);
            });

        return endpoints;
    }
}

public sealed record AccountPreferenceUpdateRequest(
    string TimestampDisplayMode);

public sealed record ExperiencePreferenceUpdateRequest(
    string PreferredUiLanguage,
    string ThemePreference,
    string TextSizePreference,
    bool HighContrastEnabled,
    bool ReduceMotionEnabled,
    string[] ExperienceFocus);

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Net.Http.Headers;

namespace FullWorth.Web.Infrastructure;

public static class HtmlNotFoundPages
{
    public static IApplicationBuilder UseFullWorthHtmlNotFoundPages(this IApplicationBuilder app)
    {
        // Only document navigation is eligible. BFF/auth/health/transport and
        // asset requests must retain their original response contracts.
        return app.UseWhen(IsPageNavigation, branch =>
        {
            branch.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            branch.Use(async (HttpContext context, RequestDelegate next) =>
            {
                await next(context);
                var feature = context.Features.Get<IStatusCodePagesFeature>();
                if (context.Response.StatusCode != StatusCodes.Status404NotFound)
                {
                    // The framework status-page middleware otherwise also
                    // handles empty 401/403/405/429/5xx responses.
                    if (feature is not null) feature.Enabled = false;
                }
                else if (!context.Response.HasStarted)
                {
                    context.Response.Headers.CacheControl = "no-store";
                }
            });
        });
    }

    private static bool IsPageNavigation(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsGet(request.Method) ||
            Path.HasExtension(request.Path.Value) ||
            new[] { "/bff", "/auth", "/health", "/assets", "/js", "/_framework", "/_blazor" }
                .Any(prefix => request.Path.StartsWithSegments(prefix)))
            return false;

        return MediaTypeHeaderValue.TryParseList(
            request.Headers.Accept.Select(value => value ?? string.Empty).ToArray(), out var accepted) &&
            accepted is not null &&
            accepted.Any(value => string.Equals(value.MediaType.Value, "text/html", StringComparison.OrdinalIgnoreCase) &&
                value.Quality.GetValueOrDefault(1) > 0);
    }
}

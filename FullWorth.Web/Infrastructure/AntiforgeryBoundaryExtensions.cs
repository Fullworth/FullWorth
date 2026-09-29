using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Routing;

namespace FullWorth.Web.Infrastructure;

public static class AntiforgeryBoundaryExtensions
{
    public static IApplicationBuilder UseFullWorthAntiforgeryBoundary(
        this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use(
            async (context, next) =>
            {
                if (!RequiresAntiforgeryValidation(context))
                {
                    await next();
                    return;
                }

                if (IsCrossOriginUnsafeRequest(
                        context,
                        out var rejectionReason))
                {
                    var logger =
                        context.RequestServices
                            .GetRequiredService<
                                ILoggerFactory>()
                            .CreateLogger(
                                "FullWorth.Web.Security.OriginBoundary");

                    logger.LogWarning(
                        "Blocked unsafe Web request at the origin boundary. RequestId={RequestId}; Reason={Reason}",
                        context.TraceIdentifier,
                        rejectionReason);

                    context.Response.StatusCode =
                        StatusCodes.Status403Forbidden;

                    return;
                }

                var antiforgery =
                    context.RequestServices.GetRequiredService<IAntiforgery>();

                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    context.Response.StatusCode =
                        StatusCodes.Status400BadRequest;

                    return;
                }

                await next();
            });
    }

    private static bool IsCrossOriginUnsafeRequest(
        HttpContext context,
        out string rejectionReason)
    {
        rejectionReason =
            string.Empty;

        var fetchSiteHeaders =
            context.Request.Headers["Sec-Fetch-Site"];

        foreach (var headerValue in fetchSiteHeaders)
        {
            if (string.IsNullOrWhiteSpace(headerValue))
            {
                continue;
            }

            if (headerValue.Split(
                    ',',
                    StringSplitOptions.TrimEntries |
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(value => string.Equals(
                    value,
                    "cross-site",
                    StringComparison.OrdinalIgnoreCase)))
            {
                rejectionReason =
                    "fetch-metadata-cross-site";

                return true;
            }
        }

        var originHeaders =
            context.Request.Headers["Origin"];

        if (originHeaders.Count == 0)
        {
            return false;
        }

        if (originHeaders.Count != 1 ||
            !Uri.TryCreate(
                originHeaders[0],
                UriKind.Absolute,
                out var origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) ||
            origin.AbsolutePath != "/")
        {
            rejectionReason =
                "origin-invalid";

            return true;
        }

        var request =
            context.Request;

        var requestPort =
            request.Host.Port ??
            (string.Equals(
                request.Scheme,
                Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)
                ? 443
                : 80);

        if (!string.Equals(
                origin.Scheme,
                request.Scheme,
                StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason =
                "origin-scheme-mismatch";

            return true;
        }

        if (!string.Equals(
                origin.IdnHost,
                request.Host.Host,
                StringComparison.OrdinalIgnoreCase))
        {
            rejectionReason =
                "origin-host-mismatch";

            return true;
        }

        if (origin.Port != requestPort)
        {
            rejectionReason =
                "origin-port-mismatch";

            return true;
        }

        return false;
    }

    private static bool RequiresAntiforgeryValidation(
        HttpContext context)
    {
        if (context.GetEndpoint() is not RouteEndpoint routeEndpoint)
        {
            return false;
        }

        var routeTemplate =
            routeEndpoint.RoutePattern.RawText;

        if (string.IsNullOrWhiteSpace(routeTemplate) ||
            (!routeTemplate.StartsWith(
                 "/bff",
                 StringComparison.OrdinalIgnoreCase) &&
             !routeTemplate.StartsWith(
                 "/auth",
                 StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        var method = context.Request.Method;

        return !HttpMethods.IsGet(method) &&
               !HttpMethods.IsHead(method) &&
               !HttpMethods.IsOptions(method) &&
               !HttpMethods.IsTrace(method);
    }
}

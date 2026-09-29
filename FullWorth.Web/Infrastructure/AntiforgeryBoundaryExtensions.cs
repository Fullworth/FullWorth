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

        if (originHeaders.Count != 1)
        {
            rejectionReason =
                "origin-header-count";

            return true;
        }

        var originValue =
            originHeaders[0];

        if (string.IsNullOrWhiteSpace(originValue))
        {
            rejectionReason =
                "origin-empty";

            return true;
        }

        if (string.Equals(
                originValue.Trim(),
                "null",
                StringComparison.OrdinalIgnoreCase))
        {
            var fetchSiteHeaders =
                context.Request.Headers["Sec-Fetch-Site"];

            if (fetchSiteHeaders.Count == 1 &&
                string.Equals(
                    fetchSiteHeaders[0]?.Trim(),
                    "same-origin",
                    StringComparison.OrdinalIgnoreCase))
            {
                // The opaque Origin is corroborated by browser-generated same-origin
                // metadata. The caller must still pass normal antiforgery validation.
                return false;
            }

            rejectionReason =
                "origin-opaque";

            return true;
        }

        if (originValue.Contains(','))
        {
            rejectionReason =
                "origin-combined-values";

            return true;
        }

        if (!Uri.TryCreate(
                originValue,
                UriKind.Absolute,
                out var origin))
        {
            rejectionReason =
                "origin-syntax-invalid";

            return true;
        }

        if (!string.IsNullOrEmpty(origin.UserInfo))
        {
            rejectionReason =
                "origin-has-user-info";

            return true;
        }

        if (!string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            rejectionReason =
                "origin-has-query-or-fragment";

            return true;
        }

        if (origin.AbsolutePath != "/")
        {
            rejectionReason =
                "origin-has-path";

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

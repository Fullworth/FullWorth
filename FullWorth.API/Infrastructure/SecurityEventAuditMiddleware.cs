using Microsoft.AspNetCore.Routing;

namespace FullWorth.API.Infrastructure;

internal static class SecurityEventNames
{
    internal const string AuthenticationRejected =
        "authentication_rejected";

    internal const string AuthorizationDenied =
        "authorization_denied";

    internal const string RateLimitRejected =
        "rate_limit_rejected";
}

internal static class SecurityEventIds
{
    internal static readonly EventId AuthenticationRejected =
        new(
            29001,
            nameof(AuthenticationRejected));

    internal static readonly EventId AuthorizationDenied =
        new(
            29002,
            nameof(AuthorizationDenied));

    internal static readonly EventId RateLimitRejected =
        new(
            29003,
            nameof(RateLimitRejected));
}

internal sealed record SecurityEventObservation(
    string Name,
    string HttpMethod,
    string EndpointPattern,
    int StatusCode,
    bool Authenticated,
    string RequestId);

internal interface ISecurityEventSink
{
    void Write(SecurityEventObservation securityEvent);
}

internal sealed class LoggerSecurityEventSink(
    ILoggerFactory loggerFactory)
    : ISecurityEventSink
{
    private readonly ILogger _logger =
        loggerFactory.CreateLogger(
            "FullWorth.SecurityEvents");

    public void Write(
        SecurityEventObservation securityEvent)
    {
        ArgumentNullException.ThrowIfNull(
            securityEvent);

        var eventId =
            securityEvent.Name switch
            {
                SecurityEventNames.AuthenticationRejected =>
                    SecurityEventIds.AuthenticationRejected,

                SecurityEventNames.AuthorizationDenied =>
                    SecurityEventIds.AuthorizationDenied,

                SecurityEventNames.RateLimitRejected =>
                    SecurityEventIds.RateLimitRejected,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(securityEvent),
                        "Unknown security event.")
            };

        _logger.LogWarning(
            eventId,
            "Security event {SecurityEventName} method={HttpMethod} endpoint={EndpointPattern} status={StatusCode} authenticated={Authenticated} request_id={RequestId}",
            securityEvent.Name,
            securityEvent.HttpMethod,
            securityEvent.EndpointPattern,
            securityEvent.StatusCode,
            securityEvent.Authenticated,
            securityEvent.RequestId);
    }
}

internal sealed class SecurityEventAuditMiddleware(
    RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        ISecurityEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        ArgumentNullException.ThrowIfNull(
            sink);

        await next(
            context);

        if (!TryCreateObservation(
                context,
                out var observation))
        {
            return;
        }

        sink.Write(
            observation);
    }

    internal static bool TryCreateObservation(
        HttpContext context,
        out SecurityEventObservation observation)
    {
        ArgumentNullException.ThrowIfNull(
            context);

        var eventName =
            context.Response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized =>
                    SecurityEventNames.AuthenticationRejected,

                StatusCodes.Status403Forbidden =>
                    SecurityEventNames.AuthorizationDenied,

                StatusCodes.Status429TooManyRequests =>
                    SecurityEventNames.RateLimitRejected,

                _ =>
                    null
            };

        if (eventName is null)
        {
            observation =
                null!;

            return false;
        }

        /*
         * Route patterns are application-owned templates. Never log the raw
         * path, query string, request body, IP address, claims, or account
         * identifiers here because those values can contain user financial
         * evidence or attacker-controlled text.
         */
        var endpointPattern =
            (context.GetEndpoint() as RouteEndpoint)?
                .RoutePattern
                .RawText
            ?? "<unmatched>";

        observation =
            new SecurityEventObservation(
                eventName,
                GetSafeHttpMethod(
                    context.Request.Method),
                endpointPattern,
                context.Response.StatusCode,
                context.User.Identity?
                    .IsAuthenticated ==
                    true,
                context.TraceIdentifier);

        return true;
    }

    private static string GetSafeHttpMethod(
        string method)
    {
        return method switch
        {
            "GET" =>
                "GET",

            "POST" =>
                "POST",

            "PUT" =>
                "PUT",

            "PATCH" =>
                "PATCH",

            "DELETE" =>
                "DELETE",

            "OPTIONS" =>
                "OPTIONS",

            "HEAD" =>
                "HEAD",

            "CONNECT" =>
                "CONNECT",

            "TRACE" =>
                "TRACE",

            _ =>
                "OTHER"
        };
    }
}

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

    internal const string AdminMutationCompleted =
        "admin_mutation_completed";

    internal const string AccountExportCompleted =
        "account_export_completed";

    internal const string AccountDeletionCompleted =
        "account_deletion_completed";

    internal const string FinancialProviderAttentionRequired =
        "financial_provider_attention_required";
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

    internal static readonly EventId AdminMutationCompleted =
        new(
            29011,
            nameof(AdminMutationCompleted));

    internal static readonly EventId AccountExportCompleted =
        new(
            29012,
            nameof(AccountExportCompleted));

    internal static readonly EventId AccountDeletionCompleted =
        new(
            29013,
            nameof(AccountDeletionCompleted));

    internal static readonly EventId FinancialProviderAttentionRequired =
        new(
            29014,
            nameof(FinancialProviderAttentionRequired));
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
    ILoggerFactory loggerFactory,
    SecurityEventAlertAggregator alertAggregator)
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

        alertAggregator.Observe(
            securityEvent);
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


internal static class SecuritySensitiveActionLog
{
    private const string SecurityLoggerCategory =
        "FullWorth.SecurityEvents";

    internal static ILogger CreateLogger(
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(
            loggerFactory);

        return loggerFactory.CreateLogger(
            SecurityLoggerCategory);
    }

    internal static void AdminMutationCompleted(
        ILogger logger,
        string action)
    {
        ArgumentNullException.ThrowIfNull(
            logger);

        var safeAction =
            action switch
            {
                "StaffRoleAssigned" =>
                    action,

                "StaffRoleRemoved" =>
                    action,

                "SubscriptionEntitlementGranted" =>
                    action,

                "SubscriptionEntitlementRevoked" =>
                    action,

                "UserProgramMembershipEnabled" =>
                    action,

                "UserProgramMembershipDisabled" =>
                    action,

                "SubscriptionAccessKeyCreated" =>
                    action,

                "SubscriptionAccessKeyRevoked" =>
                    action,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(action),
                        "Unknown administrative security action.")
            };

        logger.LogWarning(
            SecurityEventIds.AdminMutationCompleted,
            "Security event {SecurityEventName} action={SecurityAction}",
            SecurityEventNames.AdminMutationCompleted,
            safeAction);
    }

    internal static void FinancialProviderAttentionRequired(
        ILogger logger,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(
            logger);

        var safeOperation =
            operation switch
            {
                "accounts_sync" =>
                    operation,

                "transactions_sync" =>
                    operation,

                _ =>
                    throw new ArgumentOutOfRangeException(
                        nameof(operation),
                        "Unknown financial-provider security operation.")
            };

        logger.LogWarning(
            SecurityEventIds.FinancialProviderAttentionRequired,
            "Security event {SecurityEventName} provider={Provider} operation={ProviderOperation}",
            SecurityEventNames.FinancialProviderAttentionRequired,
            "plaid",
            safeOperation);
    }

    internal static void AccountExportCompleted(
        ILogger logger,
        string requestId)
    {
        WriteAccountAction(
            logger,
            SecurityEventIds.AccountExportCompleted,
            SecurityEventNames.AccountExportCompleted,
            requestId);
    }

    internal static void AccountDeletionCompleted(
        ILogger logger,
        string requestId)
    {
        WriteAccountAction(
            logger,
            SecurityEventIds.AccountDeletionCompleted,
            SecurityEventNames.AccountDeletionCompleted,
            requestId);
    }

    private static void WriteAccountAction(
        ILogger logger,
        EventId eventId,
        string eventName,
        string requestId)
    {
        ArgumentNullException.ThrowIfNull(
            logger);

        logger.LogWarning(
            eventId,
            "Security event {SecurityEventName} request_id={RequestId}",
            eventName,
            GetSafeRequestId(
                requestId));
    }

    private static string GetSafeRequestId(
        string requestId)
    {
        if (requestId.Length !=
                32 ||
            requestId.Any(
                character =>
                    !char.IsAsciiHexDigit(
                        character)))
        {
            return "<unavailable>";
        }

        return requestId.ToLowerInvariant();
    }
}

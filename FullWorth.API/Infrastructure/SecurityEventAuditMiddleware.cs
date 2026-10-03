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

    internal const string OwnershipScopedResourceNotFound =
        "ownership_scoped_resource_not_found";

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

    internal static readonly EventId OwnershipScopedResourceNotFound =
        new(
            29004,
            nameof(OwnershipScopedResourceNotFound));

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

                SecurityEventNames.OwnershipScopedResourceNotFound =>
                    SecurityEventIds.OwnershipScopedResourceNotFound,

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

        var httpMethod =
            GetSafeHttpMethod(
                context.Request.Method);

        var authenticated =
            context.User.Identity?
                .IsAuthenticated ==
            true;

        var eventName =
            context.Response.StatusCode switch
            {
                StatusCodes.Status401Unauthorized =>
                    SecurityEventNames.AuthenticationRejected,

                StatusCodes.Status403Forbidden =>
                    SecurityEventNames.AuthorizationDenied,

                StatusCodes.Status429TooManyRequests =>
                    SecurityEventNames.RateLimitRejected,

                StatusCodes.Status404NotFound
                    when authenticated &&
                         IsOwnershipScopedResourceRoute(
                             httpMethod,
                             endpointPattern) =>
                    SecurityEventNames.OwnershipScopedResourceNotFound,

                _ =>
                    null
            };

        if (eventName is null)
        {
            observation =
                null!;

            return false;
        }

        observation =
            new SecurityEventObservation(
                eventName,
                httpMethod,
                endpointPattern,
                context.Response.StatusCode,
                authenticated,
                context.TraceIdentifier);

        return true;
    }

    private static bool IsOwnershipScopedResourceRoute(
        string httpMethod,
        string endpointPattern)
    {
        return (
            httpMethod,
            endpointPattern) switch
        {
            (
                "DELETE",
                "/api/bank-connections/{connectionId:guid}") =>
                true,

            (
                "DELETE",
                "/api/planning/bill-funding-preferences/{billStreamId:guid}") =>
                true,

            (
                "GET",
                "/api/bill-streams/{billStreamId:guid}") =>
                true,

            (
                "GET",
                "/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}") =>
                true,

            (
                "GET",
                "/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file") =>
                true,

            (
                "POST",
                "/api/alerts/{alertId:guid}/dismiss") =>
                true,

            (
                "POST",
                "/api/alerts/{alertId:guid}/read") =>
                true,

            (
                "POST",
                "/api/bill-streams/{billStreamId:guid}/statement-uploads") =>
                true,

            (
                "POST",
                "/api/plaid/connections/{connectionId:guid}/accounts/sync") =>
                true,

            (
                "POST",
                "/api/plaid/connections/{connectionId:guid}/transactions/sync") =>
                true,

            (
                "POST",
                "/api/plaid/connections/{connectionId:guid}/update-link-token") =>
                true,

            (
                "POST",
                "/api/plaid/link-session/{sessionId:guid}/complete") =>
                true,

            (
                "PUT",
                "/api/planning/bill-funding-preferences/{billStreamId:guid}") =>
                true,

            (
                "PUT",
                "/api/planning/payday-plans/{payrollTransactionId:guid}") =>
                true,

            _ =>
                false
        };
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
        SecuritySensitiveActionAlertAggregator alertAggregator,
        string action)
    {
        ArgumentNullException.ThrowIfNull(
            logger);

        ArgumentNullException.ThrowIfNull(
            alertAggregator);

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

        alertAggregator.Observe(
            new SecuritySensitiveActionObservation(
                SecurityEventNames.AdminMutationCompleted,
                "action",
                safeAction));
    }

    internal static void FinancialProviderAttentionRequired(
        ILogger logger,
        SecuritySensitiveActionAlertAggregator alertAggregator,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(
            logger);

        ArgumentNullException.ThrowIfNull(
            alertAggregator);

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

        alertAggregator.Observe(
            new SecuritySensitiveActionObservation(
                SecurityEventNames.FinancialProviderAttentionRequired,
                "operation",
                safeOperation));
    }

    internal static void AccountExportCompleted(
        ILogger logger,
        SecuritySensitiveActionAlertAggregator alertAggregator,
        string requestId)
    {
        WriteAccountAction(
            logger,
            alertAggregator,
            SecurityEventIds.AccountExportCompleted,
            SecurityEventNames.AccountExportCompleted,
            requestId);
    }

    internal static void AccountDeletionCompleted(
        ILogger logger,
        SecuritySensitiveActionAlertAggregator alertAggregator,
        string requestId)
    {
        WriteAccountAction(
            logger,
            alertAggregator,
            SecurityEventIds.AccountDeletionCompleted,
            SecurityEventNames.AccountDeletionCompleted,
            requestId);
    }

    private static void WriteAccountAction(
        ILogger logger,
        SecuritySensitiveActionAlertAggregator alertAggregator,
        EventId eventId,
        string eventName,
        string requestId)
    {
        ArgumentNullException.ThrowIfNull(
            logger);

        ArgumentNullException.ThrowIfNull(
            alertAggregator);

        logger.LogWarning(
            eventId,
            "Security event {SecurityEventName} request_id={RequestId}",
            eventName,
            GetSafeRequestId(
                requestId));

        alertAggregator.Observe(
            new SecuritySensitiveActionObservation(
                eventName,
                "scope",
                "application"));
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

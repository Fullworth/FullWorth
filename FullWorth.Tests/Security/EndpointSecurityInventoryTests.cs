using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class EndpointSecurityInventoryTests
{
    private static readonly string[] ExpectedInventory =
        """
API|DELETE|/api/account|authenticated|global
API|DELETE|/api/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|global
API|DELETE|/api/bank-connections/{connectionId:guid}|authenticated|global
API|DELETE|/api/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|global
API|GET|/api/account/preferences|authenticated|global
API|GET|/api/account/security|authenticated|global
API|GET|/api/admin/access-keys|authenticated|global
API|GET|/api/admin/audit-log|authenticated|global
API|GET|/api/admin/users|authenticated|global
API|GET|/api/alerts|authenticated|global
API|GET|/api/auth/confirmEmail|anonymous-implicit|named:authentication
API|GET|/api/auth/external|authenticated|named:authentication
API|GET|/api/auth/manage/info|authenticated|named:authentication
API|GET|/api/bank-accounts|authenticated|global
API|GET|/api/bank-connections|authenticated|global
API|GET|/api/bank-transactions|authenticated|global
API|GET|/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file|authenticated|named:statement-download
API|GET|/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}|authenticated|global
API|GET|/api/bill-streams/{billStreamId:guid}|authenticated|global
API|GET|/api/bill-streams|authenticated|global
API|GET|/api/planning/bill-funding-preferences|authenticated|global
API|GET|/api/planning/pay-schedule|authenticated|global
API|GET|/api/planning/payday-plans/recent|authenticated|global
API|GET|/api/planning/upcoming-bill-changes|authenticated|global
API|GET|/api/subscription/plans|authenticated|global
API|GET|/api/subscription|authenticated|global
API|GET|/health/live|anonymous-explicit|global
API|GET|/health/ready|anonymous-explicit|global
API|GET|/openapi/{documentName}.json|anonymous-implicit|global
API|POST|/api/account/export|authenticated|named:account-export
API|POST|/api/account/security/email/verification|authenticated|global
API|POST|/api/account/security/email|authenticated|global
API|POST|/api/account/security/password|authenticated|global
API|POST|/api/account/security/profile|authenticated|global
API|POST|/api/account/security/sessions/revoke-all|authenticated|global
API|POST|/api/account/security/two-factor/disable|authenticated|global
API|POST|/api/account/security/two-factor/enable|authenticated|global
API|POST|/api/account/security/two-factor/recovery-codes|authenticated|global
API|POST|/api/account/security/two-factor/reset|authenticated|global
API|POST|/api/account/security/two-factor/setup|authenticated|global
API|POST|/api/admin/subscription/access-keys/{accessKeyId:guid}/revoke|authenticated|global
API|POST|/api/admin/subscription/access-keys|authenticated|global
API|POST|/api/admin/users/{targetUserId:guid}/entitlements/{entitlementId:guid}/revoke|authenticated|global
API|POST|/api/admin/users/{targetUserId:guid}/entitlements|authenticated|global
API|POST|/api/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|global
API|POST|/api/alerts/{alertId:guid}/dismiss|authenticated|global
API|POST|/api/alerts/{alertId:guid}/read|authenticated|global
API|POST|/api/auth/external/link|authenticated|named:authentication
API|POST|/api/auth/external/login|anonymous-explicit|named:authentication
API|POST|/api/auth/external/register|anonymous-explicit|named:authentication
API|POST|/api/auth/external/unlink|authenticated|named:authentication
API|POST|/api/auth/forgotPassword|anonymous-implicit|named:authentication
API|POST|/api/auth/login|anonymous-implicit|named:authentication
API|POST|/api/auth/logout|anonymous-explicit|named:authentication
API|POST|/api/auth/manage/2fa|authenticated|named:authentication
API|POST|/api/auth/manage/info|authenticated|named:authentication
API|POST|/api/auth/refresh|anonymous-implicit|named:authentication
API|POST|/api/auth/register|anonymous-implicit|named:authentication
API|POST|/api/auth/resendConfirmationEmail|anonymous-implicit|named:authentication
API|POST|/api/auth/resetPassword|anonymous-implicit|named:authentication
API|POST|/api/bill-discovery/run|authenticated|global
API|POST|/api/bill-monitoring/refresh|authenticated|global
API|POST|/api/bill-streams/{billStreamId:guid}/statement-uploads|authenticated|named:statement-upload
API|POST|/api/bill-streams|authenticated|global
API|POST|/api/plaid/accounts/sync|authenticated|global
API|POST|/api/plaid/connections/{connectionId:guid}/accounts/sync|authenticated|global
API|POST|/api/plaid/connections/{connectionId:guid}/transactions/sync|authenticated|global
API|POST|/api/plaid/connections/{connectionId:guid}/update-link-token|authenticated|global
API|POST|/api/plaid/exchange-public-token|authenticated|global
API|POST|/api/plaid/link-session/{sessionId:guid}/complete|authenticated|global
API|POST|/api/plaid/link-token|authenticated|global
API|POST|/api/plaid/transactions/sync|authenticated|global
API|POST|/api/subscription/access-keys/redeem|authenticated|named:subscription-redemption
API|POST|/api/subscription/billing-portal|authenticated|named:subscription-redemption
API|POST|/api/subscription/checkout|authenticated|named:subscription-redemption
API|POST|/api/subscription/sync|authenticated|named:subscription-redemption
API|POST|/api/subscription/webhooks/stripe|anonymous-explicit|global
API|PUT|/api/account/preferences/experience|authenticated|global
API|PUT|/api/account/preferences|authenticated|global
API|PUT|/api/admin/users/{targetUserId:guid}/programs/{programName}|authenticated|global
API|PUT|/api/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|global
API|PUT|/api/planning/pay-schedule|authenticated|global
API|PUT|/api/planning/payday-plans/{payrollTransactionId:guid}|authenticated|global
WEB|DELETE|/bff/account|authenticated|global
WEB|DELETE|/bff/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|global
WEB|DELETE|/bff/bank-connections/{connectionId:guid}|authenticated|global
WEB|DELETE|/bff/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|global
WEB|GET|/Error|anonymous-implicit|global
WEB|GET|/app/account/privacy|authenticated|global
WEB|GET|/app/account/settings|authenticated|global
WEB|GET|/app/account/transactions|authenticated|global
WEB|GET|/app/account|authenticated|global
WEB|GET|/app/activity|authenticated|global
WEB|GET|/app/admin|authenticated|global
WEB|GET|/app/bills/{BillStreamId:guid}|authenticated|global
WEB|GET|/app/bills|authenticated|global
WEB|GET|/app/planning|authenticated|global
WEB|GET|/app/profile|authenticated|global
WEB|GET|/app/setup|authenticated|global
WEB|GET|/app/subscription|authenticated|global
WEB|GET|/app|authenticated|global
WEB|GET|/auth/confirm-email|anonymous-implicit|global
WEB|GET|/auth/external/complete|anonymous-explicit|global
WEB|GET|/auth/external/{provider}/link|authenticated|global
WEB|GET|/auth/external/{provider}/register|anonymous-explicit|global
WEB|GET|/auth/external/{provider}|anonymous-explicit|global
WEB|GET|/bff/account/external|authenticated|global
WEB|GET|/bff/account/preferences/|authenticated|global
WEB|GET|/bff/account/security/|authenticated|global
WEB|GET|/bff/admin/access-keys|authenticated|global
WEB|GET|/bff/admin/audit-log|authenticated|global
WEB|GET|/bff/admin/users|authenticated|global
WEB|GET|/bff/alerts|authenticated|global
WEB|GET|/bff/antiforgery|authenticated|global
WEB|GET|/bff/bank-accounts|authenticated|global
WEB|GET|/bff/bank-connections|authenticated|global
WEB|GET|/bff/bank-transactions|authenticated|global
WEB|GET|/bff/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file|authenticated|global
WEB|GET|/bff/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}|authenticated|global
WEB|GET|/bff/bill-streams/{billStreamId:guid}|authenticated|global
WEB|GET|/bff/bill-streams|authenticated|global
WEB|GET|/bff/planning/bill-funding-preferences|authenticated|global
WEB|GET|/bff/planning/pay-schedule|authenticated|global
WEB|GET|/bff/planning/payday-plans/recent|authenticated|global
WEB|GET|/bff/planning/upcoming-bill-changes|authenticated|global
WEB|GET|/bff/subscription/plans|authenticated|global
WEB|GET|/bff/subscription|authenticated|global
WEB|GET|/forgot-password|anonymous-implicit|global
WEB|GET|/login|anonymous-implicit|global
WEB|GET|/not-found|anonymous-implicit|global
WEB|GET|/privacy|anonymous-implicit|global
WEB|GET|/register|anonymous-implicit|global
WEB|GET|/reset-password|anonymous-implicit|global
WEB|GET|/terms|anonymous-implicit|global
WEB|GET|/|anonymous-implicit|global
WEB|POST|/Error|anonymous-implicit|global
WEB|POST|/app/account/privacy|authenticated|global
WEB|POST|/app/account/settings|authenticated|global
WEB|POST|/app/account/transactions|authenticated|global
WEB|POST|/app/account|authenticated|global
WEB|POST|/app/activity|authenticated|global
WEB|POST|/app/admin|authenticated|global
WEB|POST|/app/bills/{BillStreamId:guid}|authenticated|global
WEB|POST|/app/bills|authenticated|global
WEB|POST|/app/planning|authenticated|global
WEB|POST|/app/profile|authenticated|global
WEB|POST|/app/setup|authenticated|global
WEB|POST|/app/subscription|authenticated|global
WEB|POST|/app|authenticated|global
WEB|POST|/auth/external/register/complete|anonymous-explicit|named:web-authentication
WEB|POST|/auth/external/two-factor|anonymous-explicit|named:web-authentication
WEB|POST|/auth/forgot-password|anonymous-implicit|named:web-authentication
WEB|POST|/auth/login|anonymous-implicit|named:web-authentication
WEB|POST|/auth/logout|anonymous-implicit|global
WEB|POST|/auth/register|anonymous-implicit|named:web-authentication
WEB|POST|/auth/reset-password|anonymous-implicit|named:web-authentication
WEB|POST|/bff/account/export|authenticated|global
WEB|POST|/bff/account/external/link|authenticated|global
WEB|POST|/bff/account/external/unlink|authenticated|global
WEB|POST|/bff/account/security/email/verification|authenticated|global
WEB|POST|/bff/account/security/email|authenticated|global
WEB|POST|/bff/account/security/password|authenticated|global
WEB|POST|/bff/account/security/profile|authenticated|global
WEB|POST|/bff/account/security/sessions/revoke-all|authenticated|global
WEB|POST|/bff/account/security/two-factor/disable|authenticated|global
WEB|POST|/bff/account/security/two-factor/enable|authenticated|global
WEB|POST|/bff/account/security/two-factor/recovery-codes|authenticated|global
WEB|POST|/bff/account/security/two-factor/reset|authenticated|global
WEB|POST|/bff/account/security/two-factor/setup|authenticated|global
WEB|POST|/bff/admin/access-keys/{accessKeyId:guid}/revoke|authenticated|global
WEB|POST|/bff/admin/access-keys|authenticated|global
WEB|POST|/bff/admin/users/{targetUserId:guid}/entitlements/{entitlementId:guid}/revoke|authenticated|global
WEB|POST|/bff/admin/users/{targetUserId:guid}/entitlements|authenticated|global
WEB|POST|/bff/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|global
WEB|POST|/bff/alerts/{alertId:guid}/dismiss|authenticated|global
WEB|POST|/bff/alerts/{alertId:guid}/read|authenticated|global
WEB|POST|/bff/bill-monitoring/refresh|authenticated|global
WEB|POST|/bff/bill-streams/{billStreamId:guid}/statement-uploads|authenticated|named:statement-upload
WEB|POST|/bff/plaid/connections/{connectionId:guid}/update-link-session|authenticated|global
WEB|POST|/bff/plaid/link-session/{sessionId:guid}/complete|authenticated|global
WEB|POST|/bff/plaid/link-session|authenticated|global
WEB|POST|/bff/subscription/access-keys/redeem|authenticated|global
WEB|POST|/bff/subscription/billing-portal|authenticated|global
WEB|POST|/bff/subscription/checkout|authenticated|global
WEB|POST|/bff/subscription/sync|authenticated|global
WEB|POST|/forgot-password|anonymous-implicit|global
WEB|POST|/login|anonymous-implicit|global
WEB|POST|/not-found|anonymous-implicit|global
WEB|POST|/privacy|anonymous-implicit|global
WEB|POST|/register|anonymous-implicit|global
WEB|POST|/reset-password|anonymous-implicit|global
WEB|POST|/terms|anonymous-implicit|global
WEB|POST|/|anonymous-implicit|global
WEB|PUT|/bff/account/preferences/experience|authenticated|global
WEB|PUT|/bff/account/preferences/|authenticated|global
WEB|PUT|/bff/admin/users/{targetUserId:guid}/programs/{programName}|authenticated|global
WEB|PUT|/bff/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|global
WEB|PUT|/bff/planning/pay-schedule|authenticated|global
        """
        .Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

    [Fact]
    public void RuntimeInventory_MatchesReviewedSecuritySnapshot()
    {
        using var apiFactory =
            new FullWorthApiFactory();

        using var apiClient =
            apiFactory.CreateHttpsClient();

        using var webFactory =
            new FullWorthWebFactory();

        using var webClient =
            webFactory.CreateHttpsClient();

        var actualInventory =
            GetRows(
                    "API",
                    apiFactory.Services)
                .Concat(
                    GetRows(
                        "WEB",
                        webFactory.Services))
                .OrderBy(
                    row =>
                        row,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.Equal(
            ExpectedInventory,
            actualInventory);
    }

    [Fact]
    public void Snapshot_CoversEveryReviewedAccessAndRateLimitClass()
    {
        Assert.Equal(
            198,
            ExpectedInventory.Length);

        Assert.Equal(
            83,
            ExpectedInventory.Count(
                row =>
                    row.StartsWith(
                        "API|",
                        StringComparison.Ordinal)));

        Assert.Equal(
            115,
            ExpectedInventory.Count(
                row =>
                    row.StartsWith(
                        "WEB|",
                        StringComparison.Ordinal)));

        Assert.Contains(
            ExpectedInventory,
            row =>
                row.Contains(
                    "|authenticated|",
                    StringComparison.Ordinal));

        Assert.Contains(
            ExpectedInventory,
            row =>
                row.Contains(
                    "|anonymous-explicit|",
                    StringComparison.Ordinal));

        Assert.Contains(
            ExpectedInventory,
            row =>
                row.Contains(
                    "|anonymous-implicit|",
                    StringComparison.Ordinal));

        Assert.Contains(
            ExpectedInventory,
            row =>
                row.Contains(
                    "|named:",
                    StringComparison.Ordinal));
    }

    private static IEnumerable<string> GetRows(
        string host,
        IServiceProvider services)
    {
        var endpointDataSource =
            services.GetRequiredService<EndpointDataSource>();

        foreach (var endpoint in
                 endpointDataSource.Endpoints
                     .OfType<RouteEndpoint>())
        {
            var httpMethods =
                endpoint.Metadata
                    .GetMetadata<HttpMethodMetadata>()?
                    .HttpMethods;

            if (httpMethods is null ||
                httpMethods.Count ==
                    0)
            {
                continue;
            }

            var route =
                "/" +
                (endpoint.RoutePattern.RawText
                    ?? "<unknown>")
                .TrimStart(
                    '/');

            if (!IsApplicationEndpoint(
                    host,
                    route))
            {
                continue;
            }

            var authentication =
                endpoint.Metadata
                    .GetMetadata<IAllowAnonymous>() is not null
                    ? "anonymous-explicit"
                    : endpoint.Metadata
                        .GetOrderedMetadata<IAuthorizeData>()
                        .Count >
                      0
                        ? "authenticated"
                        : "anonymous-implicit";

            var namedRateLimit =
                endpoint.Metadata
                    .GetOrderedMetadata<
                        EnableRateLimitingAttribute>()
                    .LastOrDefault()?
                    .PolicyName;

            var rateLimit =
                endpoint.Metadata
                    .GetMetadata<
                        DisableRateLimitingAttribute>() is not null
                    ? "disabled"
                    : namedRateLimit is null
                        ? "global"
                        : $"named:{namedRateLimit}";

            foreach (var method in
                     httpMethods.OrderBy(
                         value =>
                             value,
                         StringComparer.Ordinal))
            {
                yield return
                    $"{host}|{method}|{route}|{authentication}|{rateLimit}";
            }
        }
    }

    private static bool IsApplicationEndpoint(
        string host,
        string route)
    {
        if (host ==
            "API")
        {
            return route.StartsWith(
                       "/api/",
                       StringComparison.Ordinal) ||
                   route.StartsWith(
                       "/health/",
                       StringComparison.Ordinal) ||
                   route.StartsWith(
                       "/openapi/",
                       StringComparison.Ordinal);
        }

        return route is
                   "/"
                   or "/Error"
                   or "/forgot-password"
                   or "/login"
                   or "/not-found"
                   or "/privacy"
                   or "/register"
                   or "/reset-password"
                   or "/terms" ||
               route.StartsWith(
                   "/auth/",
                   StringComparison.Ordinal) ||
               route.StartsWith(
                   "/bff/",
                   StringComparison.Ordinal) ||
               route ==
                   "/app" ||
               route.StartsWith(
                   "/app/",
                   StringComparison.Ordinal);
    }
}

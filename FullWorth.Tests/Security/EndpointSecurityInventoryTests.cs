using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class EndpointSecurityInventoryTests
{
    private static readonly string[] ExpectedInventory =
    [
        "API|DELETE|/api/account|authenticated|global:300-per-minute",
        "API|DELETE|/api/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|global:300-per-minute",
        "API|DELETE|/api/bank-connections/{connectionId:guid}|authenticated|global:300-per-minute",
        "API|DELETE|/api/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|global:300-per-minute",
        "API|GET|/api/account/preferences|authenticated|global:300-per-minute",
        "API|GET|/api/account/security|authenticated|global:300-per-minute",
        "API|GET|/api/admin/access-keys|authenticated|global:300-per-minute",
        "API|GET|/api/admin/audit-log|authenticated|global:300-per-minute",
        "API|GET|/api/admin/users|authenticated|global:300-per-minute",
        "API|GET|/api/alerts|authenticated|global:300-per-minute",
        "API|GET|/api/auth/confirmEmail|anonymous-implicit|named:authentication",
        "API|GET|/api/auth/external|authenticated|named:authentication",
        "API|GET|/api/auth/manage/info|authenticated|named:authentication",
        "API|GET|/api/bank-accounts|authenticated|global:300-per-minute",
        "API|GET|/api/bank-connections|authenticated|global:300-per-minute",
        "API|GET|/api/bank-transactions|authenticated|global:300-per-minute",
        "API|GET|/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file|authenticated|named:statement-download",
        "API|GET|/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}|authenticated|global:300-per-minute",
        "API|GET|/api/bill-streams/{billStreamId:guid}|authenticated|global:300-per-minute",
        "API|GET|/api/bill-streams|authenticated|global:300-per-minute",
        "API|GET|/api/planning/bill-funding-preferences|authenticated|global:300-per-minute",
        "API|GET|/api/planning/pay-schedule|authenticated|global:300-per-minute",
        "API|GET|/api/planning/payday-plans/recent|authenticated|global:300-per-minute",
        "API|GET|/api/planning/upcoming-bill-changes|authenticated|global:300-per-minute",
        "API|GET|/api/subscription/plans|authenticated|global:300-per-minute",
        "API|GET|/api/subscription|authenticated|global:300-per-minute",
        "API|GET|/health/live|anonymous-explicit|global:300-per-minute",
        "API|GET|/health/ready|anonymous-explicit|global:300-per-minute",
        "API|GET|/openapi/{documentName}.json|anonymous-implicit|global:300-per-minute",
        "API|POST|/api/account/export|authenticated|named:account-export",
        "API|POST|/api/account/security/email/verification|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/email|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/password|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/profile|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/sessions/revoke-all|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/two-factor/disable|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/two-factor/enable|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/two-factor/recovery-codes|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/two-factor/reset|authenticated|global:300-per-minute",
        "API|POST|/api/account/security/two-factor/setup|authenticated|global:300-per-minute",
        "API|POST|/api/admin/subscription/access-keys/{accessKeyId:guid}/revoke|authenticated|global:300-per-minute",
        "API|POST|/api/admin/subscription/access-keys|authenticated|global:300-per-minute",
        "API|POST|/api/admin/users/{targetUserId:guid}/entitlements/{entitlementId:guid}/revoke|authenticated|global:300-per-minute",
        "API|POST|/api/admin/users/{targetUserId:guid}/entitlements|authenticated|global:300-per-minute",
        "API|POST|/api/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|global:300-per-minute",
        "API|POST|/api/alerts/{alertId:guid}/dismiss|authenticated|global:300-per-minute",
        "API|POST|/api/alerts/{alertId:guid}/read|authenticated|global:300-per-minute",
        "API|POST|/api/auth/external/link|authenticated|named:authentication",
        "API|POST|/api/auth/external/login|anonymous-explicit|named:authentication",
        "API|POST|/api/auth/external/register|anonymous-explicit|named:authentication",
        "API|POST|/api/auth/external/unlink|authenticated|named:authentication",
        "API|POST|/api/auth/forgotPassword|anonymous-implicit|named:authentication",
        "API|POST|/api/auth/login|anonymous-implicit|named:authentication",
        "API|POST|/api/auth/logout|anonymous-explicit|named:authentication",
        "API|POST|/api/auth/manage/2fa|authenticated|named:authentication",
        "API|POST|/api/auth/manage/info|authenticated|named:authentication",
        "API|POST|/api/auth/refresh|anonymous-implicit|named:authentication",
        "API|POST|/api/auth/register|anonymous-implicit|named:authentication",
        "API|POST|/api/auth/resendConfirmationEmail|anonymous-implicit|named:authentication",
        "API|POST|/api/auth/resetPassword|anonymous-implicit|named:authentication",
        "API|POST|/api/bill-discovery/run|authenticated|named:financial-refresh",
        "API|POST|/api/bill-monitoring/refresh|authenticated|named:financial-refresh",
        "API|POST|/api/bill-streams/{billStreamId:guid}/statement-uploads|authenticated|named:statement-upload",
        "API|POST|/api/bill-streams|authenticated|global:300-per-minute",
        "API|POST|/api/plaid/accounts/sync|authenticated|named:financial-refresh",
        "API|POST|/api/plaid/connections/{connectionId:guid}/accounts/sync|authenticated|named:financial-refresh",
        "API|POST|/api/plaid/connections/{connectionId:guid}/transactions/sync|authenticated|named:financial-refresh",
        "API|POST|/api/plaid/connections/{connectionId:guid}/update-link-token|authenticated|named:financial-provider",
        "API|POST|/api/plaid/exchange-public-token|authenticated|named:financial-provider",
        "API|POST|/api/plaid/link-session/{sessionId:guid}/complete|authenticated|named:financial-provider",
        "API|POST|/api/plaid/link-token|authenticated|named:financial-provider",
        "API|POST|/api/plaid/transactions/sync|authenticated|named:financial-refresh",
        "API|POST|/api/subscription/access-keys/redeem|authenticated|named:subscription-redemption",
        "API|POST|/api/subscription/billing-portal|authenticated|named:subscription-redemption",
        "API|POST|/api/subscription/checkout|authenticated|named:subscription-redemption",
        "API|POST|/api/subscription/sync|authenticated|named:subscription-redemption",
        "API|POST|/api/subscription/webhooks/stripe|anonymous-explicit|global:300-per-minute",
        "API|PUT|/api/account/preferences/experience|authenticated|global:300-per-minute",
        "API|PUT|/api/account/preferences|authenticated|global:300-per-minute",
        "API|PUT|/api/admin/users/{targetUserId:guid}/programs/{programName}|authenticated|global:300-per-minute",
        "API|PUT|/api/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|global:300-per-minute",
        "API|PUT|/api/planning/pay-schedule|authenticated|global:300-per-minute",
        "API|PUT|/api/planning/payday-plans/{payrollTransactionId:guid}|authenticated|global:300-per-minute",
        "WEB|DELETE|/bff/account|authenticated|none",
        "WEB|DELETE|/bff/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|none",
        "WEB|DELETE|/bff/bank-connections/{connectionId:guid}|authenticated|none",
        "WEB|DELETE|/bff/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|none",
        "WEB|GET|/Error|anonymous-implicit|none",
        "WEB|GET|/app/account/privacy|authenticated|none",
        "WEB|GET|/app/account/settings|authenticated|none",
        "WEB|GET|/app/account/transactions|authenticated|none",
        "WEB|GET|/app/account|authenticated|none",
        "WEB|GET|/app/activity|authenticated|none",
        "WEB|GET|/app/admin|authenticated|none",
        "WEB|GET|/app/bills/{BillStreamId:guid}|authenticated|none",
        "WEB|GET|/app/bills|authenticated|none",
        "WEB|GET|/app/planning|authenticated|none",
        "WEB|GET|/app/profile|authenticated|none",
        "WEB|GET|/app/setup|authenticated|none",
        "WEB|GET|/app/subscription|authenticated|none",
        "WEB|GET|/app|authenticated|none",
        "WEB|GET|/auth/confirm-email|anonymous-implicit|none",
        "WEB|GET|/auth/external/complete|anonymous-explicit|none",
        "WEB|GET|/auth/external/{provider}/link|authenticated|none",
        "WEB|GET|/auth/external/{provider}/register|anonymous-explicit|none",
        "WEB|GET|/auth/external/{provider}|anonymous-explicit|none",
        "WEB|GET|/bff/account/external|authenticated|none",
        "WEB|GET|/bff/account/preferences/|authenticated|none",
        "WEB|GET|/bff/account/security/|authenticated|none",
        "WEB|GET|/bff/admin/access-keys|authenticated|none",
        "WEB|GET|/bff/admin/audit-log|authenticated|none",
        "WEB|GET|/bff/admin/users|authenticated|none",
        "WEB|GET|/bff/alerts|authenticated|none",
        "WEB|GET|/bff/antiforgery|authenticated|none",
        "WEB|GET|/bff/bank-accounts|authenticated|none",
        "WEB|GET|/bff/bank-connections|authenticated|none",
        "WEB|GET|/bff/bank-transactions|authenticated|none",
        "WEB|GET|/bff/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file|authenticated|none",
        "WEB|GET|/bff/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}|authenticated|none",
        "WEB|GET|/bff/bill-streams/{billStreamId:guid}|authenticated|none",
        "WEB|GET|/bff/bill-streams|authenticated|none",
        "WEB|GET|/bff/planning/bill-funding-preferences|authenticated|none",
        "WEB|GET|/bff/planning/pay-schedule|authenticated|none",
        "WEB|GET|/bff/planning/payday-plans/recent|authenticated|none",
        "WEB|GET|/bff/planning/upcoming-bill-changes|authenticated|none",
        "WEB|GET|/bff/subscription/plans|authenticated|none",
        "WEB|GET|/bff/subscription|authenticated|none",
        "WEB|GET|/forgot-password|anonymous-implicit|none",
        "WEB|GET|/login|anonymous-implicit|none",
        "WEB|GET|/not-found|anonymous-implicit|none",
        "WEB|GET|/privacy|anonymous-implicit|none",
        "WEB|GET|/register|anonymous-implicit|none",
        "WEB|GET|/reset-password|anonymous-implicit|none",
        "WEB|GET|/terms|anonymous-implicit|none",
        "WEB|GET|/|anonymous-implicit|none",
        "WEB|POST|/Error|anonymous-implicit|none",
        "WEB|POST|/app/account/privacy|authenticated|none",
        "WEB|POST|/app/account/settings|authenticated|none",
        "WEB|POST|/app/account/transactions|authenticated|none",
        "WEB|POST|/app/account|authenticated|none",
        "WEB|POST|/app/activity|authenticated|none",
        "WEB|POST|/app/admin|authenticated|none",
        "WEB|POST|/app/bills/{BillStreamId:guid}|authenticated|none",
        "WEB|POST|/app/bills|authenticated|none",
        "WEB|POST|/app/planning|authenticated|none",
        "WEB|POST|/app/profile|authenticated|none",
        "WEB|POST|/app/setup|authenticated|none",
        "WEB|POST|/app/subscription|authenticated|none",
        "WEB|POST|/app|authenticated|none",
        "WEB|POST|/auth/external/register/complete|anonymous-explicit|named:web-authentication",
        "WEB|POST|/auth/external/two-factor|anonymous-explicit|named:web-authentication",
        "WEB|POST|/auth/forgot-password|anonymous-implicit|named:web-authentication",
        "WEB|POST|/auth/login|anonymous-implicit|named:web-authentication",
        "WEB|POST|/auth/logout|anonymous-implicit|none",
        "WEB|POST|/auth/register|anonymous-implicit|named:web-authentication",
        "WEB|POST|/auth/reset-password|anonymous-implicit|named:web-authentication",
        "WEB|POST|/bff/account/export|authenticated|none",
        "WEB|POST|/bff/account/external/link|authenticated|none",
        "WEB|POST|/bff/account/external/unlink|authenticated|none",
        "WEB|POST|/bff/account/security/email/verification|authenticated|none",
        "WEB|POST|/bff/account/security/email|authenticated|none",
        "WEB|POST|/bff/account/security/password|authenticated|none",
        "WEB|POST|/bff/account/security/profile|authenticated|none",
        "WEB|POST|/bff/account/security/sessions/revoke-all|authenticated|none",
        "WEB|POST|/bff/account/security/two-factor/disable|authenticated|none",
        "WEB|POST|/bff/account/security/two-factor/enable|authenticated|none",
        "WEB|POST|/bff/account/security/two-factor/recovery-codes|authenticated|none",
        "WEB|POST|/bff/account/security/two-factor/reset|authenticated|none",
        "WEB|POST|/bff/account/security/two-factor/setup|authenticated|none",
        "WEB|POST|/bff/admin/access-keys/{accessKeyId:guid}/revoke|authenticated|none",
        "WEB|POST|/bff/admin/access-keys|authenticated|none",
        "WEB|POST|/bff/admin/users/{targetUserId:guid}/entitlements/{entitlementId:guid}/revoke|authenticated|none",
        "WEB|POST|/bff/admin/users/{targetUserId:guid}/entitlements|authenticated|none",
        "WEB|POST|/bff/admin/users/{targetUserId:guid}/roles/{roleName}|authenticated|none",
        "WEB|POST|/bff/alerts/{alertId:guid}/dismiss|authenticated|none",
        "WEB|POST|/bff/alerts/{alertId:guid}/read|authenticated|none",
        "WEB|POST|/bff/bill-monitoring/refresh|authenticated|named:financial-refresh",
        "WEB|POST|/bff/bill-streams/{billStreamId:guid}/statement-uploads|authenticated|named:statement-upload",
        "WEB|POST|/bff/plaid/connections/{connectionId:guid}/update-link-session|authenticated|named:financial-provider",
        "WEB|POST|/bff/plaid/link-session/{sessionId:guid}/complete|authenticated|named:financial-provider",
        "WEB|POST|/bff/plaid/link-session|authenticated|named:financial-provider",
        "WEB|POST|/bff/subscription/access-keys/redeem|authenticated|none",
        "WEB|POST|/bff/subscription/billing-portal|authenticated|none",
        "WEB|POST|/bff/subscription/checkout|authenticated|none",
        "WEB|POST|/bff/subscription/sync|authenticated|none",
        "WEB|POST|/forgot-password|anonymous-implicit|none",
        "WEB|POST|/login|anonymous-implicit|none",
        "WEB|POST|/not-found|anonymous-implicit|none",
        "WEB|POST|/privacy|anonymous-implicit|none",
        "WEB|POST|/register|anonymous-implicit|none",
        "WEB|POST|/reset-password|anonymous-implicit|none",
        "WEB|POST|/terms|anonymous-implicit|none",
        "WEB|POST|/|anonymous-implicit|none",
        "WEB|PUT|/bff/account/preferences/experience|authenticated|none",
        "WEB|PUT|/bff/account/preferences/|authenticated|none",
        "WEB|PUT|/bff/admin/users/{targetUserId:guid}/programs/{programName}|authenticated|none",
        "WEB|PUT|/bff/planning/bill-funding-preferences/{billStreamId:guid}|authenticated|none",
        "WEB|PUT|/bff/planning/pay-schedule|authenticated|none",
    ];

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

        Assert.Contains(
            ExpectedInventory,
            row =>
                row.EndsWith(
                    "|global:300-per-minute",
                    StringComparison.Ordinal));

        Assert.Contains(
            ExpectedInventory,
            row =>
                row.EndsWith(
                    "|none",
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
                    : namedRateLimit is not null
                        ? $"named:{namedRateLimit}"
                        : host ==
                          "API"
                            ? "global:300-per-minute"
                            : "none";

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

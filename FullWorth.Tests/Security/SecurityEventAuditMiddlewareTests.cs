using System.Security.Claims;
using FullWorth.API.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace FullWorth.Tests.Security;

public sealed class SecurityEventAuditMiddlewareTests
{
    [Theory]
    [InlineData(
        StatusCodes.Status401Unauthorized,
        SecurityEventNames.AuthenticationRejected)]
    [InlineData(
        StatusCodes.Status403Forbidden,
        SecurityEventNames.AuthorizationDenied)]
    [InlineData(
        StatusCodes.Status429TooManyRequests,
        SecurityEventNames.RateLimitRejected)]
    public async Task SecurityResponses_EmitOneStructuredEvent(
        int statusCode,
        string expectedName)
    {
        var sink =
            new RecordingSecurityEventSink();

        var middleware =
            new SecurityEventAuditMiddleware(
                context =>
                {
                    context.Response.StatusCode =
                        statusCode;

                    return Task.CompletedTask;
                });

        var context =
            CreateContext();

        await middleware.InvokeAsync(
            context,
            sink);

        var securityEvent =
            Assert.Single(
                sink.Events);

        Assert.Equal(
            expectedName,
            securityEvent.Name);

        Assert.Equal(
            "POST",
            securityEvent.HttpMethod);

        Assert.Equal(
            "/api/bill-streams/{billStreamId:guid}",
            securityEvent.EndpointPattern);

        Assert.Equal(
            statusCode,
            securityEvent.StatusCode);

        Assert.True(
            securityEvent.Authenticated);

        Assert.Equal(
            "0123456789abcdef0123456789abcdef",
            securityEvent.RequestId);
    }

    [Fact]
    public async Task OrdinaryResponses_DoNotEmitSecurityEvents()
    {
        var sink =
            new RecordingSecurityEventSink();

        var middleware =
            new SecurityEventAuditMiddleware(
                context =>
                {
                    context.Response.StatusCode =
                        StatusCodes.Status404NotFound;

                    return Task.CompletedTask;
                });

        await middleware.InvokeAsync(
            CreateContext(
                HttpMethods.Get,
                "/api/account"),
            sink);

        Assert.Empty(
            sink.Events);
    }

    [Theory]
    [InlineData("DELETE", "/api/bank-connections/{connectionId:guid}")]
    [InlineData("DELETE", "/api/planning/bill-funding-preferences/{billStreamId:guid}")]
    [InlineData("GET", "/api/bill-streams/{billStreamId:guid}")]
    [InlineData("GET", "/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}")]
    [InlineData("GET", "/api/bill-streams/{billStreamId:guid}/statement-uploads/{uploadId:guid}/file")]
    [InlineData("POST", "/api/alerts/{alertId:guid}/dismiss")]
    [InlineData("POST", "/api/alerts/{alertId:guid}/read")]
    [InlineData("POST", "/api/bill-streams/{billStreamId:guid}/statement-uploads")]
    [InlineData("POST", "/api/plaid/connections/{connectionId:guid}/accounts/sync")]
    [InlineData("POST", "/api/plaid/connections/{connectionId:guid}/transactions/sync")]
    [InlineData("POST", "/api/plaid/connections/{connectionId:guid}/update-link-token")]
    [InlineData("POST", "/api/plaid/link-session/{sessionId:guid}/complete")]
    [InlineData("PUT", "/api/planning/bill-funding-preferences/{billStreamId:guid}")]
    [InlineData("PUT", "/api/planning/payday-plans/{payrollTransactionId:guid}")]
    public void AuthenticatedOwnershipScopedNotFound_EmitsProbeSignal(
        string method,
        string routePattern)
    {
        var context =
            CreateContext(
                method,
                routePattern);

        context.Response.StatusCode =
            StatusCodes.Status404NotFound;

        var created =
            SecurityEventAuditMiddleware.TryCreateObservation(
                context,
                out var securityEvent);

        Assert.True(
            created);

        Assert.Equal(
            SecurityEventNames.OwnershipScopedResourceNotFound,
            securityEvent.Name);

        Assert.Equal(
            method,
            securityEvent.HttpMethod);

        Assert.Equal(
            routePattern,
            securityEvent.EndpointPattern);

        Assert.Equal(
            StatusCodes.Status404NotFound,
            securityEvent.StatusCode);

        Assert.True(
            securityEvent.Authenticated);
    }

    [Fact]
    public void AnonymousOwnershipScopedNotFound_DoesNotEmitProbeSignal()
    {
        var context =
            CreateContext();

        context.User =
            new ClaimsPrincipal(
                new ClaimsIdentity());

        context.Response.StatusCode =
            StatusCodes.Status404NotFound;

        var created =
            SecurityEventAuditMiddleware.TryCreateObservation(
                context,
                out _);

        Assert.False(
            created);
    }

    [Fact]
    public void Observation_NormalizesAnAttackerControlledHttpMethod()
    {
        var context =
            CreateContext();

        context.Request.Method =
            "POST\\r\\nInjected-Field: secret";

        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        var created =
            SecurityEventAuditMiddleware.TryCreateObservation(
                context,
                out var securityEvent);

        Assert.True(
            created);

        Assert.Equal(
            "OTHER",
            securityEvent.HttpMethod);

        Assert.DoesNotContain(
            "secret",
            securityEvent.HttpMethod,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Observation_UsesRouteTemplateAndExcludesRawRequestData()
    {
        var context =
            CreateContext();

        context.Request.Path =
            "/api/bill-streams/55d5dc7c-58e8-4e24-929f-4d792c2b6880";

        context.Request.QueryString =
            new QueryString(
                "?email=sensitive%40example.com&token=secret");

        context.Request.Headers.Authorization =
            "Bearer secret-token";

        context.Response.StatusCode =
            StatusCodes.Status403Forbidden;

        var created =
            SecurityEventAuditMiddleware.TryCreateObservation(
                context,
                out var securityEvent);

        Assert.True(
            created);

        Assert.Equal(
            "/api/bill-streams/{billStreamId:guid}",
            securityEvent.EndpointPattern);

        var serializedFields =
            string.Join(
                "|",
                securityEvent.Name,
                securityEvent.HttpMethod,
                securityEvent.EndpointPattern,
                securityEvent.StatusCode,
                securityEvent.Authenticated,
                securityEvent.RequestId);

        Assert.DoesNotContain(
            "sensitive",
            serializedFields,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "secret",
            serializedFields,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "55d5dc7c",
            serializedFields,
            StringComparison.OrdinalIgnoreCase);
    }

    private static DefaultHttpContext CreateContext(
        string method = "POST",
        string routePattern = "/api/bill-streams/{billStreamId:guid}")
    {
        var context =
            new DefaultHttpContext();

        context.TraceIdentifier =
            "0123456789abcdef0123456789abcdef";

        context.Request.Method =
            method;

        context.User =
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(
                            ClaimTypes.NameIdentifier,
                            Guid.NewGuid().ToString())
                    ],
                    authenticationType:
                        "test"));

        context.SetEndpoint(
            new RouteEndpoint(
                _ =>
                    Task.CompletedTask,

                RoutePatternFactory.Parse(
                    routePattern),

                order:
                    0,

                EndpointMetadataCollection.Empty,

                displayName:
                    "test endpoint"));

        return context;
    }

    private sealed class RecordingSecurityEventSink
        : ISecurityEventSink
    {
        internal List<SecurityEventObservation> Events { get; } =
            [];

        public void Write(
            SecurityEventObservation securityEvent)
        {
            Events.Add(
                securityEvent);
        }
    }
}

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
            CreateContext(),
            sink);

        Assert.Empty(
            sink.Events);
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

    private static DefaultHttpContext CreateContext()
    {
        var context =
            new DefaultHttpContext();

        context.TraceIdentifier =
            "0123456789abcdef0123456789abcdef";

        context.Request.Method =
            HttpMethods.Post;

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
                    "/api/bill-streams/{billStreamId:guid}"),

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

using System.Net;
using FullWorth.Tests.Infrastructure;
using FullWorth.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Web;

public sealed class HtmlNotFoundRecoveryTests
{
    [Theory]
    [InlineData("/missing-ui-page", false)]
    [InlineData("/missing-ui-page", true)]
    [InlineData("/app/missing-ui-page", false)]
    [InlineData("/app/missing-ui-page", true)]
    public async Task UnknownDocument_RendersRecoveryWith404_WithoutRedirect(string route, bool authenticated)
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        if (!authenticated) client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        using var response = await client.GetAsync(route + "?untrusted=external");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("id=\"not-found-title\"", html);
        Assert.Contains("href=\"/app\"", html);
        Assert.Contains("href=\"/\"", html);
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(response.Headers.Contains("Content-Security-Policy"));
    }

    [Theory]
    [InlineData("/bff/missing", "GET", "text/html")]
    [InlineData("/auth/missing", "GET", "text/html")]
    [InlineData("/health/missing", "GET", "text/html")]
    [InlineData("/assets/missing", "GET", "text/html")]
    [InlineData("/js/missing", "GET", "text/html")]
    [InlineData("/_framework/missing", "GET", "text/html")]
    [InlineData("/_blazor/missing", "GET", "text/html")]
    [InlineData("/missing.css", "GET", "text/html")]
    [InlineData("/missing", "POST", "text/html")]
    [InlineData("/missing", "HEAD", "text/html")]
    [InlineData("/missing", "GET", "application/json")]
    [InlineData("/missing", "GET", "*/*")]
    [InlineData("/missing", "GET", "text/html;q=0, application/json")]
    [InlineData("/missing", "GET", "invalid accept header")]
    [InlineData("/missing", "GET", "")]
    public async Task NonDocumentRequests_DoNotReexecuteOrReturnHtml(string path, string method, string accept)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseFullWorthHtmlNotFoundPages();
        var calls = 0;
        app.Run(context => { calls++; context.Response.StatusCode = 404; return Task.CompletedTask; });
        var context = Context(services, path, method, accept);
        await app.Build()(context);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal(1, calls);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.Null(context.Response.ContentType);
        Assert.Equal(path, context.Request.Path.Value);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(405)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task OtherErrors_KeepStatusAndHeaders_WithoutReexecution(int status)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseFullWorthHtmlNotFoundPages();
        var calls = 0;
        app.Run(context =>
        {
            calls++;
            context.Response.StatusCode = status;
            context.Response.Headers.RetryAfter = "45";
            return Task.CompletedTask;
        });
        var context = Context(services, "/missing", "GET", "text/html");
        await app.Build()(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal("45", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal(1, calls);
        Assert.Equal(0, context.Response.Body.Length);
        Assert.False(context.Response.Headers.ContainsKey("Cache-Control"));
    }

    [Fact]
    public async Task ExistingProblemResponse_IsPreserved()
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseFullWorthHtmlNotFoundPages();
        var calls = 0;
        app.Run(async context =>
        {
            calls++;
            context.Response.StatusCode = 404;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync("{\"title\":\"Not found\"}");
        });
        var context = Context(services, "/missing", "GET", "text/html");
        await app.Build()(context);
        Assert.Equal(1, calls);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        Assert.Equal("{\"title\":\"Not found\"}", await new StreamReader(context.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/json, text/html;q=0.5")]
    public async Task Html404_ReexecutesOnce_AndRestoresOriginalRequest(string accept)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseFullWorthHtmlNotFoundPages();
        var paths = new List<string>();
        app.Run(async context =>
        {
            paths.Add(context.Request.Path.Value!);
            if (context.Request.Path == "/not-found")
            {
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("Recovery");
            }
            else context.Response.StatusCode = 404;
        });
        var context = Context(services, "/missing", "GET", accept);
        context.Request.QueryString = new QueryString("?original=1");
        await app.Build()(context);
        Assert.Equal(new[] { "/missing", "/not-found" }, paths);
        Assert.Equal("/missing", context.Request.Path.Value);
        Assert.Equal("?original=1", context.Request.QueryString.Value);
        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("no-store", context.Response.Headers.CacheControl.ToString());
    }

    [Fact]
    public async Task AnonymousBffChallenge_Remains401WithoutHtml()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html");
        using var response = await client.GetAsync("/bff/bill-streams");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("not-found-title", await response.Content.ReadAsStringAsync());
    }

    private static DefaultHttpContext Context(IServiceProvider services, string path, string method, string accept)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = path;
        context.Request.Method = method;
        context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();
        return context;
    }
}

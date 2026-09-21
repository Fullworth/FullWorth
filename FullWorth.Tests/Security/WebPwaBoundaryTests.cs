using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebPwaBoundaryTests
{
    [Fact]
    public async Task Manifest_ProvidesStandaloneInstallMetadata()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/site.webmanifest");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        using var document =
            JsonDocument.Parse(
                body);

        var root =
            document.RootElement;

        Assert.Equal(
            "FullWorth",
            root.GetProperty(
                    "name")
                .GetString());

        Assert.Equal(
            "/app",
            root.GetProperty(
                    "start_url")
                .GetString());

        Assert.Equal(
            "/",
            root.GetProperty(
                    "scope")
                .GetString());

        Assert.Equal(
            "standalone",
            root.GetProperty(
                    "display")
                .GetString());

        var iconSizes =
            root.GetProperty(
                    "icons")
                .EnumerateArray()
                .Select(icon =>
                    icon.GetProperty(
                            "sizes")
                        .GetString())
                .ToArray();

        Assert.Contains(
            "192x192",
            iconSizes);

        Assert.Contains(
            "512x512",
            iconSizes);
    }

    [Fact]
    public async Task ServiceWorker_RemainsNetworkFirstAndDoesNotCacheFinancialRoutes()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/service-worker.js");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.Contains(
            "\"/offline.html\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "request.mode !== \"navigate\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "return await fetch(",
            body,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            Regex.Matches(
                    body,
                    @"cache\.add\(")
                .Count);

        Assert.DoesNotContain(
            "cache.put",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "/bff/",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "/api/",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "localStorage",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "sessionStorage",
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MobileNavigation_KeepsAccountAsAPrimaryDestination()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var layout =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "Components",
                    "Layout",
                    "AppLayout.razor"));

        var shellStyles =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "app-shell.css"));

        Assert.Contains(
            """
            <NavLink href="/app/account"
                         class="mobile-bottom-link"
            """,
            layout,
            StringComparison.Ordinal);

        Assert.Contains(
            "grid-template-columns: repeat(5, minmax(0, 1fr));",
            shellStyles,
            StringComparison.Ordinal);

        var settings =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "Components",
                    "Pages",
                    "App",
                    "AccountSettings.razor"));

        Assert.Contains(
            "href=\"/app/account/privacy\"",
            settings,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallHelper_IsPublicAndKeepsWorkerUpdatesOutOfHttpCache()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/js/pwa.js");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.Contains(
            "FullWorthPwa",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "beforeinstallprompt",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "updateViaCache: \"none\"",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/bff/",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "/api/",
            body,
            StringComparison.OrdinalIgnoreCase);
    }
    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "FullWorth.slnx")) &&
                Directory.Exists(
                    Path.Combine(
                        directory.FullName,
                        "FullWorth.Web")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the FullWorth repository root.");
    }
}

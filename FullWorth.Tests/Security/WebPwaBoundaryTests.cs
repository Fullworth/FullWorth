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

        Assert.Equal(
            "#0B1F3B",
            root.GetProperty(
                    "theme_color")
                .GetString());

        Assert.Equal(
            "#0B1F3B",
            root.GetProperty(
                    "background_color")
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

        Assert.Single(
            Regex.Matches(
                body,
                @"cache\.add\("));

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

        Assert.Contains(
            "\"SKIP_WAITING\"",
            body,
            StringComparison.Ordinal);

        Assert.Single(
            Regex.Matches(
                body,
                @"self\.skipWaiting\(\)"));
    }

    [Fact]
    public void ThemeScripts_SynchronizeBrowserChromeWithSelectedTheme()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var bootstrap =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "theme-bootstrap.js"));

        var theme =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "theme.js"));

        foreach (var script in new[] { bootstrap, theme })
        {
            Assert.Contains(
                "meta[name=\"theme-color\"]",
                script,
                StringComparison.Ordinal);

            Assert.Contains(
                "#0B1F3B",
                script,
                StringComparison.Ordinal);

            Assert.Contains(
                "#F7F8FB",
                script,
                StringComparison.Ordinal);
        }
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

        Assert.Matches(
            @"<NavLink\s+href=""/app/account""\s+class=""mobile-bottom-link""",
            layout);

        Assert.Contains(
            "grid-template-columns: repeat(5, minmax(0, 1fr));",
            shellStyles,
            StringComparison.Ordinal);

        Assert.Matches(
            @"<form\s+method=""post""\s+action=""/auth/logout""\s+class=""mobile-logout-form"">",
            layout);

        Assert.Equal(
            2,
            Regex.Matches(
                    layout,
                    @"<AntiforgeryToken\s*/>")
                .Count);

        Assert.Contains(
            "mobile-logout-action",
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
    public void BffClient_RedirectsExpiredSessionsAndKeepsReadsNoStore()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var bff =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "bff.js"));

        Assert.Contains(
            "response.status === 401",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "window.location.assign(",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"/login\"",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "popupClosedPendingChecks",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "popupClosedPendingChecks >=",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "text.popupClosed",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "credentials:",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"same-origin\"",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"no-store\"",
            bff,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AccountExportClient_UsesReauthenticatedPostWithoutMisclassifyingCredentialFailures()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var bff =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "bff.js"));

        var exportStart =
            bff.IndexOf(
                "export async function downloadAccountExport(",
                StringComparison.Ordinal);

        var exportEnd =
            bff.IndexOf(
                "export async function deleteFullWorthAccount(",
                exportStart,
                StringComparison.Ordinal);

        Assert.True(
            exportStart >= 0 &&
            exportEnd > exportStart);

        var exportClient =
            bff[exportStart..exportEnd];

        Assert.Contains(
            "getAntiforgeryToken()",
            exportClient,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"POST\"",
            exportClient,
            StringComparison.Ordinal);

        Assert.Contains(
            "currentPassword",
            exportClient,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"X-CSRF-TOKEN\"",
            exportClient,
            StringComparison.Ordinal);

        var credentialFailureIndex =
            exportClient.IndexOf(
                "current password is incorrect",
                StringComparison.Ordinal);

        var redirectIndex =
            exportClient.IndexOf(
                "window.location.assign(",
                StringComparison.Ordinal);

        Assert.True(
            credentialFailureIndex >= 0 &&
            redirectIndex > credentialFailureIndex,
            "Expected export credential failures to be handled before the expired-session redirect.");
    }

    [Fact]
    public void StatementUpload_RemainsBrowserFileBasedAndNoStore()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var uploadPanel =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "Components",
                    "Shared",
                    "StatementUploadPanel.razor"));

        var bff =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "bff.js"));

        Assert.Contains(
            "accept=\".pdf,.jpg,.jpeg,.png,application/pdf,image/jpeg,image/png\"",
            uploadPanel,
            StringComparison.Ordinal);

        Assert.Contains(
            "private const long MaximumFileSize = 15L * 1024 * 1024;",
            uploadPanel,
            StringComparison.Ordinal);

        Assert.Contains(
            "new FormData()",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "formData.append(",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"X-CSRF-TOKEN\"",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "cache:",
            bff,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"no-store\"",
            bff,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "FileReader",
            bff,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "arrayBuffer(",
            bff,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PerformanceProfile_UsesIdleStaticStylePrefetchWithoutFinancialRequests()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/js/performance-profile.js");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.Contains(
            "\"/account-settings.css\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"/account-transactions.css\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"/account-privacy.css\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"/subscription.css\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"requestIdleCallback\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "link.rel =",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"prefetch\"",
            body,
            StringComparison.Ordinal);

        Assert.Matches(
            @"fullworthPerformance\s*===\s*""efficiency""",
            body);

        Assert.DoesNotContain(
            "/bff/",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "/api/",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotMatch(
            @"(?i)(?<![A-Za-z0-9_])fetch\s*\(",
            body);
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
    [Fact]
    public async Task AuthenticatedVisualSystem_DoesNotUseTinyTextSizes()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/fullworth-v2.css");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content
                .ReadAsStringAsync();

        Assert.DoesNotContain(
            "font-size: 0.5",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "font-size: 0.6",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "--fw-faint: #778397;",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "--fw-faint: #93a0b5;",
            body,
            StringComparison.Ordinal);
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

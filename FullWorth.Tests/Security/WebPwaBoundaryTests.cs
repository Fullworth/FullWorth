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
            "#060910",
            root.GetProperty(
                    "theme_color")
                .GetString());

        Assert.Equal(
            "#060910",
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

        var localTime =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "local-time.js"));

        foreach (var script in new[] { bootstrap, theme, localTime })
        {
            Assert.Contains(
                "meta[name=\"theme-color\"]",
                script,
                StringComparison.Ordinal);

            Assert.Contains(
                "#060910",
                script,
                StringComparison.Ordinal);

            Assert.Contains(
                "#F4F6FB",
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

        Assert.Contains("loginRedirectUrl(window.location)", bff, StringComparison.Ordinal);
        var loginRedirect = File.ReadAllText(Path.Combine(repositoryRoot, "FullWorth.Web", "wwwroot", "js", "login-redirect.js"));
        Assert.Contains("\"/login\"", loginRedirect, StringComparison.Ordinal);

        var polling = File.ReadAllText(Path.Combine(repositoryRoot, "FullWorth.Web", "wwwroot", "js", "plaid-completion.js"));
        Assert.Contains(
            "popupClosedPendingChecks",
            polling,
            StringComparison.Ordinal);

        Assert.Contains(
            "popupClosedPendingChecks >=",
            polling,
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
        var root = FindRepositoryRoot();
        var bff = File.ReadAllText(Path.Combine(root, "FullWorth.Web", "wwwroot", "js", "bff.js"));
        var exportClient = File.ReadAllText(Path.Combine(root, "FullWorth.Web", "wwwroot", "js", "account-export.js"));
        var privacyRequests = File.ReadAllText(Path.Combine(root, "FullWorth.Web", "wwwroot", "js", "account-privacy-requests.js"));

        // Existing callers keep the same signature, without eagerly loading privacy
        // download dependencies for every unrelated BFF import.
        var exportStart = bff.IndexOf("export async function downloadAccountExport(", StringComparison.Ordinal);
        Assert.True(exportStart >= 0);
        var exportEnd = bff.IndexOf("export async function deleteFullWorthAccount(", exportStart, StringComparison.Ordinal);
        Assert.True(exportEnd > exportStart);
        var wrapper = bff[exportStart..exportEnd];
        Assert.Contains("import(\"./account-export.js\")", wrapper, StringComparison.Ordinal);
        Assert.Contains("module.downloadAccountExport(currentPassword, twoFactorCode)", wrapper, StringComparison.Ordinal);

        Assert.Contains("privacyAntiforgeryToken(operation.signal)", exportClient, StringComparison.Ordinal);
        Assert.Contains("\"POST\"", exportClient, StringComparison.Ordinal);
        Assert.Contains("currentPassword", exportClient, StringComparison.Ordinal);
        Assert.Contains("twoFactorCode", exportClient, StringComparison.Ordinal);
        Assert.Contains("\"X-CSRF-TOKEN\"", exportClient, StringComparison.Ordinal);
        Assert.Contains("\"same-origin\"", exportClient, StringComparison.Ordinal);
        Assert.Contains("\"no-store\"", exportClient, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", privacyRequests, StringComparison.Ordinal);

        var credentialFailureIndex = privacyRequests.IndexOf("current password is incorrect", StringComparison.Ordinal);
        var sessionFailureIndex = privacyRequests.IndexOf("response.status === 401", StringComparison.Ordinal);
        Assert.True(credentialFailureIndex >= 0 && sessionFailureIndex > credentialFailureIndex,
            "Credential rejection must be classified before a generic expired-session result.");
        Assert.DoesNotContain("window.location.assign(", exportClient, StringComparison.Ordinal);
        Assert.Contains("blob.size === 0", exportClient, StringComparison.Ordinal);
        Assert.Contains("cancelAccountExport", exportClient, StringComparison.Ordinal);
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
    public void WhatsNewDialog_UsesPublishedNotesWhileDeploymentIdentityRemainsVerified()
    {
        var repositoryRoot = FindRepositoryRoot();
        var dialog = File.ReadAllText(Path.Combine(
            repositoryRoot, "FullWorth.Web", "Components", "Layout", "WhatsNewDialog.razor"));
        var layout = File.ReadAllText(Path.Combine(
            repositoryRoot, "FullWorth.Web", "Components", "Layout", "AppLayout.razor"));
        var client = File.ReadAllText(Path.Combine(
            repositoryRoot, "FullWorth.Web", "wwwroot", "js", "whats-new.js"));
        var productionCompose = File.ReadAllText(Path.Combine(
            repositoryRoot, "compose.production.yml"));

        Assert.Contains("<WhatsNewDialog />", layout, StringComparison.Ordinal);

        // The dialog must never announce generic changes just because the
        // immutable deployment SHA advanced. Only authored release notes
        // with a new ID are announcements.
        Assert.Contains("OnNotesAvailable", dialog, StringComparison.Ordinal);
        Assert.Contains("_notes.Highlights", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Configuration[\"BILLWATCH_RELEASE_ID\"]",
            dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Sharper bill intelligence",
            dialog, StringComparison.Ordinal);

        Assert.Contains("\"/release-notes.json\"", client, StringComparison.Ordinal);
        Assert.Contains("cache: \"no-store\"", client, StringComparison.Ordinal);
        Assert.Contains("\"fullworth.whats-new.seen-notes\"",
            client, StringComparison.Ordinal);
        Assert.Contains("localStorage.getItem", client, StringComparison.Ordinal);
        Assert.Contains("localStorage.setItem", client, StringComparison.Ordinal);
        Assert.Contains("visibilitychange", client, StringComparison.Ordinal);
        Assert.Contains("pageshow", client, StringComparison.Ordinal);
        Assert.Contains("focus", client, StringComparison.Ordinal);
        Assert.Contains("setInterval", client, StringComparison.Ordinal);

        // The production runtime still pins its deployment release SHA.
        Assert.Contains(
            "BILLWATCH_RELEASE_ID: ${BILLWATCH_RELEASE_ID:?Set BILLWATCH_RELEASE_ID}",
            productionCompose, StringComparison.Ordinal);
        Assert.DoesNotContain("/bff/", dialog, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/", dialog, StringComparison.OrdinalIgnoreCase);
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

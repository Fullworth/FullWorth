using System.Globalization;
using System.Net;
using FullWorth.Web.Components.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using State = FullWorth.Web.Components.Shared.BankConnectionSummary.HealthState;

namespace FullWorth.Tests.Web;

public sealed class BankConnectionSummaryTests
{
    [Theory]
    [InlineData(0, false, State.AwaitingSync)]
    [InlineData(0, true, State.Connected)]
    [InlineData(1, false, State.Reconnect)]
    [InlineData(1, true, State.Reconnect)]
    [InlineData(2, false, State.Ended)]
    [InlineData(2, true, State.Ended)]
    [InlineData(99, true, State.Unknown)]
    [InlineData(-1, false, State.Unknown)]
    public void StatusIsNotAHealthGuarantee(int status, bool synced, State expected) =>
        Assert.Equal(expected, BankConnectionSummary.Evaluate([new(status, synced ? DateTimeOffset.UnixEpoch : null)]));

    [Fact]
    public void LoadingErrorStalenessAndEmptyAreDistinct()
    {
        BankConnectionSummary.Connection[] rows = [new(0, DateTimeOffset.UtcNow)];
        Assert.Equal(State.Loading, BankConnectionSummary.Evaluate(rows, loading: true));
        Assert.Equal(State.Unavailable, BankConnectionSummary.Evaluate(rows, error: true));
        Assert.Equal(State.Stale, BankConnectionSummary.Evaluate(rows, stale: true));
        Assert.Equal(State.Empty, BankConnectionSummary.Evaluate([]));
        Assert.Equal(State.Reconnect, BankConnectionSummary.Evaluate([new(0, DateTimeOffset.UtcNow), new(1, null)]));
        Assert.Equal(State.Unknown, BankConnectionSummary.Evaluate([new(0, DateTimeOffset.UtcNow), new(99, null)]));
    }

    [Fact]
    public void MixedActiveSyncIsNotTheFirstSyncOrFullCoverage()
    {
        var synced = new BankConnectionSummary.Connection(0, DateTimeOffset.UnixEpoch);
        var awaiting = new BankConnectionSummary.Connection(0, null);
        var disconnectedWithHistory = new BankConnectionSummary.Connection(2, DateTimeOffset.UnixEpoch);
        Assert.Equal(State.PartialSync, BankConnectionSummary.Evaluate([synced, awaiting]));
        Assert.Equal(State.PartialSync, BankConnectionSummary.Evaluate([awaiting, disconnectedWithHistory, synced]));
        Assert.Equal(State.AwaitingSync, BankConnectionSummary.Evaluate([awaiting, disconnectedWithHistory]));
        Assert.Equal(State.Connected, BankConnectionSummary.Evaluate([synced, disconnectedWithHistory]));
        Assert.Equal(State.Reconnect, BankConnectionSummary.Evaluate([synced, awaiting, new(1, null)]));
    }

    [Theory]
    [InlineData("en", "Some banks have not synced", "1 of 2 active banks report a successful sync")]
    [InlineData("es", "Algunos bancos aún no se han sincronizado", "1 de 2 bancos activos informan")]
    public async Task MixedActiveSyncRendersHonestLocalizedCoverage(string culture, string heading, string coverage)
    {
        var html = WebUtility.HtmlDecode(await Render(culture,
            [new(0, DateTimeOffset.UnixEpoch), new(0, null), new(2, DateTimeOffset.UnixEpoch)]));
        Assert.Contains(heading, html);
        Assert.Contains(coverage, html);
        Assert.DoesNotContain("Awaiting first sync", html);
        Assert.DoesNotContain("Reported connected", html);
    }

    [Theory]
    [InlineData("en", "Connections ended")]
    [InlineData("es", "Conexiones finalizadas")]
    public async Task EndedConnectionsAreNeverRenderedAsHealthy(string culture, string expected)
    {
        var html = WebUtility.HtmlDecode(await Render(culture, [new(2, DateTimeOffset.UtcNow)]));
        Assert.Contains(expected, html);
        Assert.DoesNotContain("is-healthy", html);
        Assert.DoesNotContain("Connections look healthy", html);
        Assert.DoesNotContain("Reported connected", html);
    }

    [Theory]
    [InlineData(true, false, false, "Checking connection state")]
    [InlineData(false, true, false, "Connection state unavailable")]
    [InlineData(false, false, true, "Previously loaded state")]
    public async Task UnverifiedSnapshotsAreExplicit(bool loading, bool error, bool stale, string expected)
    {
        var html = WebUtility.HtmlDecode(await Render("en", [new(0, DateTimeOffset.UtcNow)], loading, error, stale));
        Assert.Contains(expected, html);
        Assert.DoesNotContain("Reported connected", html);
        Assert.DoesNotContain("is-healthy", html);
        if (loading || error) Assert.Contains("—", html);
    }

    private static async Task<string> Render(string culture, IReadOnlyList<BankConnectionSummary.Connection> connections,
        bool loading = false, bool error = false, bool stale = false)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            await using var provider = services.BuildServiceProvider();
            await using var renderer = new HtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
            return await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var rendered = await renderer.RenderComponentAsync<BankConnectionSummary>(ParameterView.FromDictionary(
                    new Dictionary<string, object?> { ["Connections"] = connections, ["AccountCount"] = 1,
                        ["IsLoading"] = loading, ["HasError"] = error, ["IsStale"] = stale }));
                return rendered.ToHtmlString();
            });
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}

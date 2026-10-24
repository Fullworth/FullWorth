using System.Reflection;
using FullWorth.Web.Components.Pages.App;
using FullWorth.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class OverviewRefreshTests
{
    [Fact]
    public async Task InitialFailure_IsErrorNotAnEmptyHealthySnapshot()
    {
        using var harness = new Harness();
        harness.Module.Failures.Add("getBillStreams");
        await harness.Call("LoadAsync");
        Assert.False(harness.Get<bool>("_hasLoadedData"));
        Assert.False(string.IsNullOrWhiteSpace(harness.Get<string?>("_errorMessage")));
        Assert.False(harness.Get<bool>("_isLoading"));
    }

    [Fact]
    public async Task RefreshFailure_PreservesWholeSnapshotAndRetryRecovers()
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        var old = harness.Get<OverviewBillCoverage>("_summary");
        harness.Module.Values["getBankConnections"] = Array.Empty<Overview.BankConnectionViewModel>();
        harness.Module.Failures.Add("getBillStreams");
        await harness.Call("CheckStatusAsync");
        Assert.Same(old, harness.Get<OverviewBillCoverage>("_summary"));
        Assert.True(harness.Get<bool>("HasActiveConnection"));
        Assert.False(string.IsNullOrWhiteSpace(harness.Get<string?>("_refreshError")));
        Assert.False(harness.Get<bool>("_isCheckingStatus"));
        harness.Module.Failures.Clear();
        await harness.Call("CheckStatusAsync");
        Assert.Null(harness.Get<string?>("_refreshError"));
        Assert.False(harness.Get<bool>("HasActiveConnection"));
        Assert.Equal("$29.00", harness.Get<OverviewBillCoverage>("_summary").RecognizedTotal);
        Assert.All(harness.Module.Calls, name => Assert.StartsWith("get", name));
    }

    [Theory]
    [InlineData("getBillStreams")]
    [InlineData("getBankConnections")]
    public async Task NullRead_IsNotSilentlyConvertedToEmpty(string method)
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        var old = harness.Get<OverviewBillCoverage>("_summary");
        harness.Module.Values[method] = null;
        await harness.Call("CheckStatusAsync");
        Assert.Same(old, harness.Get<OverviewBillCoverage>("_summary"));
        Assert.NotNull(harness.Get<string?>("_refreshError"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrDuplicateIdentity_IsRejectedWithoutLosingData(bool duplicate)
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        var old = harness.Get<OverviewBillCoverage>("_summary");
        var bill = Harness.Bill();
        if (!duplicate) bill.Id = Guid.Empty;
        harness.Module.Values["getBillStreams"] = duplicate
            ? new[] { bill, bill } : new[] { bill };
        await harness.Call("CheckStatusAsync");
        Assert.Same(old, harness.Get<OverviewBillCoverage>("_summary"));
        Assert.NotNull(harness.Get<string?>("_refreshError"));
    }

    [Fact]
    public async Task PreferenceFailure_DoesNotHideReadableFinancialData()
    {
        using var harness = new Harness();
        harness.Module.Failures.Add("getExperiencePreferences");
        await harness.Call("LoadAsync");
        Assert.True(harness.Get<bool>("_hasLoadedData"));
        Assert.Null(harness.Get<string?>("_errorMessage"));
        Assert.Equal("$29.00", harness.Get<OverviewBillCoverage>("_summary").RecognizedTotal);
    }

    [Fact]
    public async Task GenuineEmptyRead_ReplacesPreviousSnapshot()
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        harness.Module.Values["getBillStreams"] = Array.Empty<Overview.BillStreamViewModel>();
        await harness.Call("CheckStatusAsync");
        Assert.Equal(0, harness.Get<OverviewBillCoverage>("_summary").ActiveCount);
        Assert.Null(harness.Get<string?>("_refreshError"));
    }

    [Fact]
    public async Task DisposedPage_DoesNotCommitDelayedResponseOrThrowRefreshFailure()
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        var old = harness.Get<OverviewBillCoverage>("_summary");
        var delayed = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Module.DelayedBills = delayed.Task;
        var refresh = harness.Call("CheckStatusAsync");
        await harness.Page.DisposeAsync();
        delayed.SetResult(Array.Empty<Overview.BillStreamViewModel>());
        await refresh;
        Assert.Same(old, harness.Get<OverviewBillCoverage>("_summary"));
        Assert.Null(harness.Get<string?>("_refreshError"));
    }

    [Fact]
    public async Task InvalidNullElement_IsRejectedAndPreservesSnapshot()
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        var old = harness.Get<OverviewBillCoverage>("_summary");
        harness.Module.Values["getBillStreams"] = new Overview.BillStreamViewModel[] { null! };
        await harness.Call("CheckStatusAsync");
        Assert.Same(old, harness.Get<OverviewBillCoverage>("_summary"));
        Assert.NotNull(harness.Get<string?>("_refreshError"));
    }

    [Fact]
    public async Task UnrepresentableTotal_IsNotCommittedAsPartialData()
    {
        using var harness = new Harness();
        await harness.Call("LoadAsync");
        var old = harness.Get<OverviewBillCoverage>("_summary");
        var bill = Harness.Bill();
        bill.CurrentAmount = decimal.MaxValue;
        harness.Module.Values["getBillStreams"] = new[] { bill, Harness.Bill() };
        await harness.Call("CheckStatusAsync");
        Assert.Same(old, harness.Get<OverviewBillCoverage>("_summary"));
        Assert.NotNull(harness.Get<string?>("_refreshError"));
    }

    [Fact]
    public async Task Watchlist_PrioritizesComparableIncreasesWithoutCrossCurrencyAmountRanking()
    {
        using var harness = new Harness();
        var names = new[] { "Zeta", "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Eta", "Theta" };
        var bills = names.Select((name, index) => new Overview.BillStreamViewModel
        {
            Id = Guid.NewGuid(), ProviderName = name, IsActive = true,
            CurrencyCode = index % 2 == 0 ? "JPY" : "USD",
            HasTransactionEvidence = index != 0,
            HasMonthlyCadenceEvidence = true,
            CurrentAmount = index % 2 == 0 ? 20000m : 40m,
            PreviousAverage = index == 1 ? 20m : (index % 2 == 0 ? 20000m : 40m)
        }).ToArray();
        harness.Module.Values["getBillStreams"] = bills;
        await harness.Call("LoadAsync");
        var selected = harness.Get<IReadOnlyList<Overview.BillStreamViewModel>>("WatchlistBills");
        Assert.Equal(6, selected.Count);
        Assert.Equal("Alpha", selected[0].ProviderName);
        Assert.Equal(new[] { "Alpha", "Beta", "Delta", "Epsilon", "Eta", "Gamma" },
            selected.Select(item => item.ProviderName).ToArray());
        Assert.DoesNotContain(selected, item => item.ProviderName == "Zeta");
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task MixedConnectionNoBills_StillSurfacesRepairWarning(
        bool waitingForFirstSync, bool brokenSecondConnection)
    {
        using var harness = new Harness();
        harness.Module.Values["getBillStreams"] = Array.Empty<Overview.BillStreamViewModel>();
        harness.Module.Values["getBankConnections"] = new[]
        {
            new Overview.BankConnectionViewModel
            {
                Id = Guid.NewGuid(), Status = 0,
                LastSuccessfulSyncAtUtc = waitingForFirstSync ? null : DateTimeOffset.UtcNow
            },
            new Overview.BankConnectionViewModel
            {
                Id = Guid.NewGuid(), Status = brokenSecondConnection ? 1 : 0,
                LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow
            }
        };
        await harness.Call("LoadAsync");

        Assert.True(harness.Get<bool>("HasActiveConnection"));
        // A first sync is pending only when NO active bank has succeeded.
        // An attention-only second bank does not count as successfully synced.
        Assert.Equal(waitingForFirstSync && brokenSecondConnection,
            harness.Get<bool>("IsWaitingForFirstSync"));
        Assert.Equal(brokenSecondConnection, harness.Get<bool>("HasConnectionNeedingAttention"));
        Assert.Equal(brokenSecondConnection,
            harness.Get<bool>("ShouldShowMixedConnectionWarning"));
    }

    [Fact]
    public async Task AttentionOnlyNoBills_UsesExistingReconnectStateInsteadOfDuplicateWarning()
    {
        using var harness = new Harness();
        harness.Module.Values["getBillStreams"] = Array.Empty<Overview.BillStreamViewModel>();
        harness.Module.Values["getBankConnections"] =
            new[] { new Overview.BankConnectionViewModel { Id = Guid.NewGuid(), Status = 1 } };
        await harness.Call("LoadAsync");
        Assert.True(harness.Get<bool>("HasConnectionNeedingAttention"));
        Assert.False(harness.Get<bool>("HasActiveConnection"));
        Assert.False(harness.Get<bool>("ShouldShowMixedConnectionWarning"));
    }

    [Fact]
    public async Task MixedConnectionsWithBills_UsesExistingBillStateWarning()
    {
        using var harness = new Harness();
        harness.Module.Values["getBankConnections"] = new[]
        {
            new Overview.BankConnectionViewModel { Id = Guid.NewGuid(), Status = 0 },
            new Overview.BankConnectionViewModel { Id = Guid.NewGuid(), Status = 1 }
        };
        await harness.Call("LoadAsync");
        Assert.True(harness.Get<bool>("HasConnectionNeedingAttention"));
        Assert.False(harness.Get<bool>("ShouldShowMixedConnectionWarning"));
    }

    private sealed class Harness : IDisposable
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly ServiceProvider _provider;
        public Overview Page { get; } = new();
        public FakeModule Module { get; } = new();

        public Harness()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = "Resources");
            _provider = services.BuildServiceProvider();
            typeof(Overview).GetProperty("L", Flags)!.SetValue(Page,
                _provider.GetRequiredService<IStringLocalizer<Overview>>());
            typeof(Overview).GetField("_bffModule", Flags)!.SetValue(Page, Module);
            Module.Values["getBillStreams"] = new[] { Bill() };
            Module.Values["getBankConnections"] = new[] {
                new Overview.BankConnectionViewModel { Id = Guid.NewGuid(), Status = 0 }
            };
            Module.Values["getExperiencePreferences"] = new Overview.ExperiencePreferenceViewModel();
        }

        public static Overview.BillStreamViewModel Bill() => new()
        {
            Id = Guid.NewGuid(), IsActive = true, ProviderName = "Example",
            HasTransactionEvidence = true, HasMonthlyCadenceEvidence = true,
            CurrencyCode = "USD", CurrentAmount = 29m, PreviousAverage = 20m
        };

        public async Task Call(string method) =>
            await (Task)typeof(Overview).GetMethod(method, Flags)!.Invoke(Page, null)!;

        public T Get<T>(string name)
        {
            var field = typeof(Overview).GetField(name, Flags);
            return (T)(field is not null ? field.GetValue(Page) :
                typeof(Overview).GetProperty(name, Flags)!.GetValue(Page))!;
        }

        public void Dispose() => _provider.Dispose();
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public Dictionary<string, object?> Values { get; } = [];
        public HashSet<string> Failures { get; } = [];
        public List<string> Calls { get; } = [];
        public Task<object?>? DelayedBills { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier,
            CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            if (Failures.Contains(identifier)) throw new JSException("Controlled failure");
            var value = identifier == "getBillStreams" && DelayedBills is not null
                ? await DelayedBills : Values[identifier];
            return (TValue)value!;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

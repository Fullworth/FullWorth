using System.Reflection;
using FullWorth.Web.Components.Pages.App;
using FullWorth.Web.Components.Shared;
using FullWorth.Web.Services;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class BillWorkspaceLifecycleTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class Module : IJSObjectReference
    {
        public Func<string, Task<object?>> Response { get; set; } = _ => Task.FromResult<object?>(null);
        public List<string> Calls { get; } = [];
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken token, object?[]? args)
        {
            Calls.Add(identifier);
            return (TValue)(await Response(identifier))!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Localizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static T Page<T>(Module module) where T : new()
    {
        var page = new T();
        Set(page!, "_bffModule", module);
        typeof(T).GetProperty("L", Flags)!.SetValue(page, new Localizer<T>());
        return page;
    }
    private static void Set(object page, string field, object? value) =>
        page.GetType().GetField(field, Flags)!.SetValue(page, value);
    private static T Get<T>(object page, string field) => (T)page.GetType().GetField(field, Flags)!.GetValue(page)!;
    private static Task Call(object page, string method) => (Task)page.GetType().GetMethod(method, Flags)!.Invoke(page, null)!;
    private static void Route(BillDetail page, Guid id)
    {
        typeof(BillDetail).GetProperty(nameof(BillDetail.BillStreamId))!.SetValue(page, id);
        typeof(BillDetail).GetMethod("OnParametersSet", Flags)!.Invoke(page, null);
        Set(page, "_requestedBillId", (Guid?)id);
    }
    private static Bills.BillStreamViewModel Row() => new()
    {
        Id = Guid.NewGuid(), ProviderName = "Saved utility", IsActive = true, Source = "Manual",
        CurrencyCode = "USD", CurrentAmount = 20, PreviousAverage = 10,
        HasTransactionEvidence = true, HasMonthlyCadenceEvidence = true
    };
    private static BillDetail.BillStreamDetailViewModel Detail(Guid id) => new()
    {
        Id = id, ProviderName = "Saved utility", CurrencyCode = "USD", CurrentAmount = 20,
        PreviousAverage = 10, HasTransactionEvidence = true, Source = "AutomaticDiscovery"
    };

    [Theory]
    [InlineData("throw")]
    [InlineData("null")]
    [InlineData("duplicate")]
    [InlineData("empty-id")]
    public async Task Bills_InvalidRefreshPreservesCompleteSnapshotAndFilters(string failure)
    {
        var row = Row();
        var module = new Module { Response = _ => Task.FromResult<object?>(new[] { row }) };
        var page = Page<Bills>(module);
        await Call(page, "LoadAsync");
        var original = Get<BillLedgerSnapshot>(page, "_snapshot");
        Set(page, "_search", "utility");
        module.Response = _ => failure switch
        {
            "throw" => Task.FromException<object?>(new JSException("unavailable")),
            "duplicate" => Task.FromResult<object?>(new[] { row, row }),
            "empty-id" => Task.FromResult<object?>(new[] { new Bills.BillStreamViewModel() }),
            _ => Task.FromResult<object?>(null)
        };
        await Call(page, "LoadAsync");
        Assert.Same(original, Get<BillLedgerSnapshot>(page, "_snapshot"));
        Assert.Equal("utility", Get<string>(page, "_search"));
        Assert.NotNull(Get<string?>(page, "_refreshError"));
        Assert.Null(Get<string?>(page, "_errorMessage"));
        Assert.False(Get<bool>(page, "_isLoading"));
        Assert.All(module.Calls, call => Assert.Equal("getBillStreams", call));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Bills_InitialFailureIsErrorAndSuccessfulEmptyRefreshReplacesOldEvidence()
    {
        var module = new Module { Response = _ => Task.FromException<object?>(new JSException("unavailable")) };
        var page = Page<Bills>(module);
        await Call(page, "LoadAsync");
        Assert.False(Get<bool>(page, "_hasSnapshot"));
        Assert.NotNull(Get<string?>(page, "_errorMessage"));
        module.Response = _ => Task.FromResult<object?>(new[] { Row() });
        await Call(page, "LoadAsync");
        Assert.True(Get<bool>(page, "_hasSnapshot"));
        module.Response = _ => Task.FromResult<object?>(Array.Empty<Bills.BillStreamViewModel>());
        await Call(page, "LoadAsync");
        Assert.Equal(0, Get<BillLedgerSnapshot>(page, "_snapshot").TotalCount);
        Assert.Null(Get<string?>(page, "_refreshError"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Bills_DisposalDiscardsLateReadAndPrivateSearch()
    {
        var response = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module { Response = _ => response.Task };
        var page = Page<Bills>(module);
        Set(page, "_search", "private utility");
        var pending = Call(page, "LoadAsync");
        await page.DisposeAsync();
        response.SetResult(new[] { Row() });
        await pending;
        Assert.Equal(0, Get<BillLedgerSnapshot>(page, "_snapshot").TotalCount);
        Assert.Equal("", Get<string>(page, "_search"));
        Assert.Single(module.Calls);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, 2)]
    public async Task Diagnostics_InvalidCountsPreservePriorEvidence(int imported, int eligible)
    {
        var valid = new BillDiscoverySummary.Snapshot { ImportedTransactions = 10 };
        var module = new Module { Response = _ => Task.FromResult<object?>(valid) };
        var page = Page<Bills>(module);
        Set(page, "_showDiscovery", true);
        await Call(page, "LoadDiscoveryAsync");
        module.Response = _ => Task.FromResult<object?>(new BillDiscoverySummary.Snapshot
            { ImportedTransactions = imported, EligiblePostedTransactions = eligible });
        await Call(page, "LoadDiscoveryAsync");
        Assert.Same(valid, Get<BillDiscoverySummary.Snapshot>(page, "_discovery"));
        Assert.True(Get<bool>(page, "_discoveryError"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Diagnostics_ClosingPanelRejectsLateResponse()
    {
        var response = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module { Response = _ => response.Task };
        var page = Page<Bills>(module);
        Set(page, "_showDiscovery", true);
        var pending = Call(page, "LoadDiscoveryAsync");
        await Call(page, "ToggleDiscoveryAsync");
        response.SetResult(new BillDiscoverySummary.Snapshot { ImportedTransactions = 99 });
        await pending;
        Assert.False(Get<bool>(page, "_showDiscovery"));
        Assert.Null(Get<BillDiscoverySummary.Snapshot?>(page, "_discovery"));
        await page.DisposeAsync();
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("null")]
    [InlineData("wrong-id")]
    [InlineData("null-history")]
    public async Task Detail_InvalidRefreshPreservesSameBillEvidence(string failure)
    {
        var id = Guid.NewGuid();
        var original = Detail(id);
        var module = new Module { Response = _ => Task.FromResult<object?>(original) };
        var page = Page<BillDetail>(module);
        Route(page, id);
        await Call(page, "LoadAsync");
        module.Response = _ => failure switch
        {
            "throw" => Task.FromException<object?>(new JSException("unavailable")),
            "wrong-id" => Task.FromResult<object?>(Detail(Guid.NewGuid())),
            "null-history" => Task.FromResult<object?>(new BillDetail.BillStreamDetailViewModel { Id = id, Statements = null! }),
            _ => Task.FromResult<object?>(null)
        };
        await Call(page, "LoadAsync");
        Assert.Same(original, Get<BillDetail.BillStreamDetailViewModel>(page, "_detail"));
        Assert.NotNull(Get<string?>(page, "_refreshError"));
        Assert.False(Get<bool>(page, "_isLoading"));
        Assert.Null(Get<string?>(page, "_errorMessage"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Detail_ConfirmedWriteSurvivesFailedReadAndRetryIsReadOnly()
    {
        var id = Guid.NewGuid();
        var original = Detail(id);
        var module = new Module { Response = _ => Task.FromResult<object?>(original) };
        var page = Page<BillDetail>(module);
        Route(page, id);
        await Call(page, "LoadAsync");
        module.Response = method => method == "confirmBillStream"
            ? Task.FromResult<object?>(null)
            : Task.FromException<object?>(new JSException("read unavailable"));
        await Call(page, "KeepMonitoringAsync");
        Assert.True(Get<BillDetail.BillStreamDetailViewModel>(page, "_detail").IsActive);
        Assert.Equal("Manual", original.Source);
        Assert.Equal("Your monitoring choice was saved.", Get<string>(page, "_keepMessage"));
        Assert.NotNull(Get<string?>(page, "_refreshError"));
        module.Response = _ => Task.FromResult<object?>(original);
        await Call(page, "ReloadAsync");
        Assert.Null(Get<string?>(page, "_refreshError"));
        Assert.Equal(1, module.Calls.Count(method => method == "confirmBillStream"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Detail_UncertainWriteCannotBeReplayedBeforeAuthoritativeRefresh()
    {
        var id = Guid.NewGuid();
        var original = Detail(id);
        var module = new Module { Response = _ => Task.FromResult<object?>(original) };
        var page = Page<BillDetail>(module);
        Route(page, id);
        await Call(page, "LoadAsync");
        module.Response = _ => Task.FromException<object?>(new JSException("response lost"));
        await Call(page, "KeepMonitoringAsync");
        await Call(page, "KeepMonitoringAsync");
        Assert.True(Get<bool>(page, "_confirmationUncertain"));
        Assert.False(original.IsActive);
        Assert.Contains("could not confirm", Get<string>(page, "_keepMessage"));
        Assert.Equal(1, module.Calls.Count(method => method == "confirmBillStream"));
        module.Response = _ => Task.FromResult<object?>(original);
        await Call(page, "LoadAsync");
        Assert.False(Get<bool>(page, "_confirmationUncertain"));
        Assert.Null(Get<string?>(page, "_keepMessage"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Detail_RouteChangeDiscardsOldReadWithoutErasingNewBill()
    {
        var id = Guid.NewGuid();
        var delayed = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module { Response = _ => delayed.Task };
        var page = Page<BillDetail>(module);
        Route(page, id);
        var pending = Call(page, "LoadAsync");
        var next = Detail(Guid.NewGuid());
        Route(page, next.Id);
        Assert.Null(Get<BillDetail.BillStreamDetailViewModel?>(page, "_detail"));
        module.Response = _ => Task.FromResult<object?>(next);
        await Call(page, "LoadAsync");
        delayed.SetResult(Detail(id));
        await pending;
        Assert.Same(next, Get<BillDetail.BillStreamDetailViewModel>(page, "_detail"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task Detail_DisposalDiscardsLateConfirmation()
    {
        var id = Guid.NewGuid();
        var original = Detail(id);
        var delayed = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = new Module { Response = _ => Task.FromResult<object?>(original) };
        var page = Page<BillDetail>(module);
        Route(page, id);
        await Call(page, "LoadAsync");
        module.Response = _ => delayed.Task;
        var pending = Call(page, "KeepMonitoringAsync");
        await page.DisposeAsync();
        delayed.SetResult(null);
        await pending;
        Assert.False(original.IsActive);
        Assert.Null(Get<BillDetail.BillStreamDetailViewModel?>(page, "_detail"));
        Assert.Null(Get<string?>(page, "_keepMessage"));
    }

    [Fact]
    public void Detail_LatestChangeFollowsStatementPeriodNotBackfillDetectionTime()
    {
        var page = new BillDetail();
        var olderId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var historicalBackfill = new BillDetail.BillChangeViewModel
        {
            Id = Guid.NewGuid(), CurrentStatementId = olderId,
            DetectedAtUtc = new DateTimeOffset(2026, 10, 22, 12, 0, 0, TimeSpan.Zero)
        };
        var latestBillingChange = new BillDetail.BillChangeViewModel
        {
            Id = Guid.NewGuid(), CurrentStatementId = newerId,
            DetectedAtUtc = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)
        };
        Set(page, "_detail", new BillDetail.BillStreamDetailViewModel
        {
            Id = Guid.NewGuid(),
            Statements =
            [
                new() { Id = olderId, PeriodStart = new DateOnly(2026, 2, 1), PeriodEnd = new DateOnly(2026, 2, 28) },
                new() { Id = newerId, PeriodStart = new DateOnly(2026, 6, 1), PeriodEnd = new DateOnly(2026, 6, 30) }
            ],
            Changes = [historicalBackfill, latestBillingChange]
        });

        var selected = (BillDetail.BillChangeViewModel?)typeof(BillDetail)
            .GetProperty("LatestChange", Flags)!.GetValue(page);
        Assert.Same(latestBillingChange, selected);

        var ordered = (IReadOnlyList<BillDetail.BillChangeViewModel>)typeof(BillDetail)
            .GetProperty("StatementChangesByPeriod", Flags)!.GetValue(page)!;
        Assert.Equal(new[] { latestBillingChange, historicalBackfill }, ordered);
    }

    [Fact]
    public void Detail_UnknownStatementLinkCannotBecomeProminentVerifiedChange()
    {
        var page = new BillDetail();
        Set(page, "_detail", new BillDetail.BillStreamDetailViewModel
        {
            Id = Guid.NewGuid(),
            Changes =
            [
                new()
                {
                    Id = Guid.NewGuid(), CurrentStatementId = Guid.NewGuid(),
                    DetectedAtUtc = DateTimeOffset.UtcNow
                }
            ]
        });
        Assert.Null(typeof(BillDetail).GetProperty("LatestChange", Flags)!.GetValue(page));
    }

    [Fact]
    public void Detail_CorrectedSamePeriodUsesMostRecentlyDetectedEvidence()
    {
        var page = new BillDetail();
        var firstId = Guid.NewGuid();
        var correctionId = Guid.NewGuid();
        var older = new BillDetail.BillChangeViewModel
        {
            Id = Guid.NewGuid(), CurrentStatementId = firstId,
            DetectedAtUtc = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)
        };
        var corrected = new BillDetail.BillChangeViewModel
        {
            Id = Guid.NewGuid(), CurrentStatementId = correctionId,
            DetectedAtUtc = new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero)
        };
        Set(page, "_detail", new BillDetail.BillStreamDetailViewModel
        {
            Id = Guid.NewGuid(),
            Statements =
            [
                new() { Id = firstId, PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31) },
                new() { Id = correctionId, PeriodStart = new DateOnly(2026, 7, 1), PeriodEnd = new DateOnly(2026, 7, 31) }
            ],
            Changes = [older, corrected]
        });
        Assert.Same(corrected,
            typeof(BillDetail).GetProperty("LatestChange", Flags)!.GetValue(page));
    }
}

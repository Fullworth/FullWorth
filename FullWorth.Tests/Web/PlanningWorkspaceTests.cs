using System.Collections;
using System.Reflection;
using System.Text.Json;
using FullWorth.Web.Components.Pages.App;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class PlanningWorkspaceTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string Schedule = """{"frequency":"Biweekly","anchorPayDate":"2026-10-16","defaultPaychecksAhead":2}""";
    private const string BillId = "d8d88252-55c1-444c-9496-e0b63a941a61";
    private const string Bills = """[{"id":"d8d88252-55c1-444c-9496-e0b63a941a61","providerName":"Example Utility","isActive":true,"currentAmount":89.25,"currencyCode":"USD","hasTransactionEvidence":true}]""";
    private const string Prefs = """[{"billStreamId":"d8d88252-55c1-444c-9496-e0b63a941a61","paychecksAheadOverride":3}]""";
    private sealed class Module : IJSObjectReference
    {
        public Func<string, object?[]?, Task<string?>> Reply { get; set; } = (name, _) => Task.FromResult<string?>(name switch {
            "getPlanningPaySchedule" => Schedule, "getBillStreams" => Bills,
            "getPlanningBillFundingPreferences" => Prefs, _ => "[]"
        });
        public List<(string Name, object?[]? Args)> Calls { get; } = [];
        public int Disposals { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            Calls.Add((name, args));
            var json = await Reply(name, args);
            return json is null ? default! : JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
    private sealed class Text : IStringLocalizer<Planning>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] args] => new(name, string.Format(name, args));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
    private sealed class Runtime(Task<IJSObjectReference> module) : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args) => (T)(object)await module;
    }
    private static Planning Page(Module module)
    {
        var page = new Planning();
        Set(page, "_bffModule", module);
        typeof(Planning).GetProperty("L", Flags)!.SetValue(page, new Text());
        return page;
    }
    private static void Set(object target, string name, object? value)
    {
        var type = target.GetType();
        var field = type.GetField(name, Flags);
        if (field is not null) field.SetValue(target, value);
        else type.GetProperty(name, Flags)!.SetValue(target, value);
    }
    private static T Get<T>(object target, string name)
    {
        var type = target.GetType();
        var field = type.GetField(name, Flags);
        return (T)(field is not null ? field.GetValue(target)! : type.GetProperty(name, Flags)!.GetValue(target)!)!;
    }
    private static Task Run(Planning page, string method, params object?[] args) => (Task)typeof(Planning).GetMethod(method, Flags)!.Invoke(page, args)!;
    private static void Act(Planning page, string method, params object?[] args) => typeof(Planning).GetMethod(method, Flags)!.Invoke(page, args);
    private static object Bill(Planning page) => Get<IList>(page, "_bills")[0]!;
    private static void EditSchedule(Planning page) => Set(page, "_defaultPaychecksAhead", "4");

    [Theory]
    [InlineData("getPlanningPaySchedule", "schedule")]
    [InlineData("getBillStreams", "bills")]
    [InlineData("getPlanningBillFundingPreferences", "bills")]
    [InlineData("getUpcomingBillChanges", "changes")]
    [InlineData("getRecentPaydayPlans", "recent")]
    public async Task FailedRead_IsIsolatedAndRetryDoesNotEraseOtherDrafts(string failed, string panel)
    {
        var module = new Module();
        var normal = module.Reply;
        module.Reply = (name, args) => name == failed ? Task.FromException<string?>(new JSException("private error")) : normal(name, args);
        var page = Page(module);
        await Run(page, "LoadAsync");
        Assert.Equal(panel, Assert.Single(Get<HashSet<string>>(page, "_loadErrors")));
        if (panel != "schedule") EditSchedule(page);
        var calls = module.Calls.Count;
        module.Reply = normal;
        await Run(page, panel switch { "schedule" => "RetryScheduleAsync", "bills" => "RetryBillsAsync", "changes" => "RetryChangesAsync", _ => "RetryRecentAsync" });
        Assert.Empty(Get<HashSet<string>>(page, "_loadErrors"));
        Assert.Equal(panel == "bills" ? 2 : 1, module.Calls.Count - calls);
        if (panel != "schedule") Assert.Equal("4", Get<string>(page, "_defaultPaychecksAhead"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task PendingHistory_DoesNotBlockScheduleOrBillResults()
    {
        var module = new Module();
        var normal = module.Reply;
        var delayed = new TaskCompletionSource<string?>();
        module.Reply = (name, args) => name == "getRecentPaydayPlans" ? delayed.Task : normal(name, args);
        var page = Page(module);
        var pending = Run(page, "LoadAsync");
        Assert.False(Get<bool>(page, "_isInitializing"));
        Assert.Equal("recent", Assert.Single(Get<HashSet<string>>(page, "_loading")));
        Assert.NotNull(Bill(page));
        EditSchedule(page);
        Assert.True(Get<bool>(page, "ScheduleDirty"));
        await Run(page, "RetryRecentAsync");
        Assert.Equal(1, module.Calls.Count(c => c.Name == "getRecentPaydayPlans"));
        delayed.SetResult("[]");
        await pending;
        await page.DisposeAsync();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("27")]
    [InlineData("bad")]
    public async Task InvalidSchedule_DoesNotWrite(string input)
    {
        var module = new Module(); var page = Page(module);
        await Run(page, "LoadAsync");
        Set(page, "_defaultPaychecksAhead", input);
        await Run(page, "SaveScheduleAsync");
        Assert.DoesNotContain(module.Calls, c => c.Name == "putPlanningPaySchedule");
        Assert.True(Get<bool>(page, "_scheduleNoticeError"));
    }

    [Fact]
    public async Task ScheduleSave_LocksDuplicateEventsAndResetAndSnapshotsPayload()
    {
        var module = new Module(); var page = Page(module);
        await Run(page, "LoadAsync"); EditSchedule(page);
        var delayed = new TaskCompletionSource<string?>();
        module.Reply = (_, _) => delayed.Task;
        var pending = Run(page, "SaveScheduleAsync");
        Act(page, "ResetSchedule"); await Run(page, "SaveScheduleAsync");
        Assert.Equal("4", Get<string>(page, "_defaultPaychecksAhead"));
        var call = Assert.Single(module.Calls, c => c.Name == "putPlanningPaySchedule");
        var payload = JsonSerializer.Serialize(call.Args![0]);
        Assert.Contains("\"defaultPaychecksAhead\":4", payload);
        delayed.SetResult(null); await pending;
        Assert.False(Get<bool>(page, "ScheduleDirty"));
        Assert.False(Get<bool>(page, "_scheduleUncertain"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UncertainSchedule_RequiresReadBeforeAnotherWriteAndKeepsDraft(bool matching)
    {
        var module = new Module(); var page = Page(module);
        await Run(page, "LoadAsync"); EditSchedule(page);
        module.Reply = (_, _) => Task.FromException<string?>(new JSDisconnectedException("lost"));
        await Run(page, "SaveScheduleAsync");
        Act(page, "ResetSchedule"); await Run(page, "SaveScheduleAsync"); await Run(page, "CheckScheduleAsync");
        Assert.True(Get<bool>(page, "_scheduleUncertain"));
        Assert.Equal("4", Get<string>(page, "_defaultPaychecksAhead"));
        Assert.Equal(1, module.Calls.Count(c => c.Name == "putPlanningPaySchedule"));
        module.Reply = (_, _) => Task.FromResult<string?>(matching ? Schedule.Replace(":2", ":4") : Schedule);
        await Run(page, "CheckScheduleAsync");
        Assert.False(Get<bool>(page, "_scheduleUncertain"));
        Assert.Equal(!matching, Get<bool>(page, "ScheduleDirty"));
        Assert.Equal("4", Get<string>(page, "_defaultPaychecksAhead"));
        Assert.Equal(1, module.Calls.Count(c => c.Name == "putPlanningPaySchedule"));
        if (!matching) { Act(page, "ResetSchedule"); Assert.Equal("2", Get<string>(page, "_defaultPaychecksAhead")); }
    }

    [Theory]
    [InlineData("2", "putPlanningBillFundingPreference")]
    [InlineData("", "deletePlanningBillFundingPreference")]
    public async Task BillSave_UsesOnlySelectedBillAndConfirmsUncertainOutcomeWithoutReplay(string selected, string write)
    {
        var module = new Module(); var page = Page(module);
        await Run(page, "LoadAsync");
        var bill = Bill(page); Set(bill, "SelectedOverride", selected);
        module.Reply = (_, _) => Task.FromException<string?>(new JSException("lost"));
        await Run(page, "SaveBillPreferenceAsync", bill);
        await Run(page, "SaveBillPreferenceAsync", bill); Act(page, "ResetBill", bill);
        Assert.Equal(selected, Get<string>(bill, "SelectedOverride"));
        Assert.True(Get<bool>(bill, "SaveUncertain"));
        var call = Assert.Single(module.Calls, c => c.Name == write);
        Assert.Equal(BillId, call.Args![0]);
        module.Reply = (_, _) => Task.FromResult<string?>(selected.Length == 0 ? "[]" : Prefs.Replace(":3", ":2"));
        await Run(page, "CheckBillAsync", bill);
        Assert.False(Get<bool>(bill, "SaveUncertain"));
        Assert.False(Get<bool>(bill, "IsDirty"));
        Assert.Single(module.Calls, c => c.Name == write);
    }

    [Fact]
    public async Task BillResetAndSearch_DoNotChangeAmountsOrStoredBaseline()
    {
        var module = new Module(); var page = Page(module);
        await Run(page, "LoadAsync");
        var bill = Bill(page); Set(bill, "SelectedOverride", "8");
        Set(page, "_billSearch", "missing");
        Assert.Empty(Get<IEnumerable>(page, "FilteredBills").Cast<object>());
        Set(page, "_billSearch", "UTILITY");
        Assert.Single(Get<IEnumerable>(page, "FilteredBills").Cast<object>());
        Act(page, "ResetBill", bill);
        Assert.Equal("3", Get<string>(bill, "SelectedOverride"));
        Assert.Equal(89.25m, Get<decimal>(bill, "CurrentAmount"));
        Assert.Equal("USD", Get<string>(bill, "CurrencyCode"));
        Assert.DoesNotContain(module.Calls, c => c.Name.StartsWith("put") || c.Name.StartsWith("delete"));
    }

    [Fact]
    public async Task MissingProviderName_UsesFallbackAndRemainsSearchableWithoutCrashing()
    {
        var module = new Module(); var normal = module.Reply;
        module.Reply = (name, args) => name == "getBillStreams"
            ? Task.FromResult<string?>(Bills.Replace("\"Example Utility\"", "null")) : normal(name, args);
        var page = Page(module); await Run(page, "LoadAsync");
        Assert.Empty(Get<HashSet<string>>(page, "_loadErrors"));
        Assert.Equal(string.Empty, Get<string>(Bill(page), "ProviderName"));
        Set(page, "_billSearch", "utility");
        Assert.Empty(Get<IEnumerable>(page, "FilteredBills").Cast<object>());
        Set(page, "_billSearch", string.Empty);
        Assert.Single(Get<IEnumerable>(page, "FilteredBills").Cast<object>());
    }

    [Theory]
    [InlineData("read")]
    [InlineData("save")]
    public async Task Disposal_RejectsLateResultsAndReleasesModuleOnce(string stage)
    {
        var module = new Module(); var page = Page(module);
        if (stage == "save") { await Run(page, "LoadAsync"); EditSchedule(page); }
        var delayed = new TaskCompletionSource<string?>();
        module.Reply = (_, _) => delayed.Task;
        var pending = Run(page, stage == "read" ? "RetryScheduleAsync" : "SaveScheduleAsync");
        await page.DisposeAsync(); await page.DisposeAsync();
        delayed.SetResult(Schedule); await pending;
        Assert.Equal(1, module.Disposals);
        if (stage == "read") Assert.Null(Get<object?>(page, "_savedSchedule"));
        else Assert.True(Get<bool>(page, "ScheduleDirty"));
    }

    [Fact]
    public async Task LateModuleAfterDisposal_IsReleasedWithoutAnyRequests()
    {
        var module = new Module(); var page = Page(module); Set(page, "_bffModule", null);
        var delayed = new TaskCompletionSource<IJSObjectReference>();
        typeof(Planning).GetProperty("JS", Flags)!.SetValue(page, new Runtime(delayed.Task));
        var pending = Run(page, "LoadAsync");
        await page.DisposeAsync(); delayed.SetResult(module); await pending;
        Assert.Equal(1, module.Disposals); Assert.Empty(module.Calls);
    }

    [Theory]
    [InlineData("getPlanningPaySchedule", """{"anchorPayDate":"2026-10-16","defaultPaychecksAhead":2}""", "schedule")]
    [InlineData("getPlanningPaySchedule", """{"frequency":"Weekly","anchorPayDate":"2026-10-16"}""", "schedule")]
    [InlineData("getPlanningPaySchedule", """{"frequency":"Unknown","anchorPayDate":"2026-10-16","defaultPaychecksAhead":2}""", "schedule")]
    [InlineData("getPlanningPaySchedule", """{"frequency":"Weekly","anchorPayDate":"invalid","defaultPaychecksAhead":2}""", "schedule")]
    [InlineData("getRecentPaydayPlans", "{}", "recent")]
    [InlineData("getUpcomingBillChanges", "null", "changes")]
    public async Task InvalidRead_IsNotRenderedAsSavedDefaultsOrEmptyEvidence(string method, string response, string panel)
    {
        var module = new Module(); var normal = module.Reply;
        module.Reply = (name, args) => name == method ? Task.FromResult<string?>(response) : normal(name, args);
        var page = Page(module); await Run(page, "LoadAsync");
        Assert.Equal(panel, Assert.Single(Get<HashSet<string>>(page, "_loadErrors")));
        Assert.False(Get<bool>(page, "_isInitializing"));
    }

    private static bool RequiresReview(object plan) =>
        (bool)typeof(Planning)
            .GetMethod("RequiresPlanReview", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [plan])!;

    private static string History(string? coverage) =>
        """[{"id":"a53b2252-52be-4748-974d-243b2a3b82d8","paycheckPostedDate":"2026-10-02","paycheckAmount":1000,"currencyCode":"USD","recommendedSetAside":0,"paycheckRemainingAfterPlan":1000,"shortfall":0,"items":[],"coverage":"""
        + (coverage ?? "null") + "}]";

    [Theory]
    [InlineData(null, true)]
    [InlineData("{\"eligibleBillCount\":1,\"missingStatementFactCount\":0,\"missingDueDateCount\":0,\"currencyMismatchCount\":0,\"invalidStatementAmountCount\":0,\"priorCurrencyConflictCount\":0}", false)]
    [InlineData("{\"eligibleBillCount\":0,\"missingStatementFactCount\":1,\"missingDueDateCount\":1,\"currencyMismatchCount\":0,\"invalidStatementAmountCount\":0,\"priorCurrencyConflictCount\":0}", true)]
    public async Task SavedPaydayCoverage_DistinguishesLegacyCompleteAndExcludedPlans(
        string? coverage, bool needsReview)
    {
        var module = new Module(); var normal = module.Reply;
        module.Reply = (name, args) => name == "getRecentPaydayPlans"
            ? Task.FromResult<string?>(History(coverage)) : normal(name, args);
        var page = Page(module);
        await Run(page, "LoadAsync");

        Assert.Empty(Get<HashSet<string>>(page, "_loadErrors"));
        var plan = Assert.Single(Get<IList>(page, "_recentPlans").Cast<object>());
        Assert.Equal(needsReview, RequiresReview(plan));
        var storedCoverage = Get<object?>(plan, "Coverage");
        if (coverage is null) Assert.Null(storedCoverage);
        else
        {
            Assert.NotNull(storedCoverage);
            Assert.Equal(needsReview ? 2 : 0, Get<int>(storedCoverage, "SkippedBillCount"));
        }

        await page.DisposeAsync();
    }

    [Theory]
    [InlineData("{\"eligibleBillCount\":-1,\"missingStatementFactCount\":0,\"missingDueDateCount\":0,\"currencyMismatchCount\":0,\"invalidStatementAmountCount\":0,\"priorCurrencyConflictCount\":0}")]
    [InlineData("{\"eligibleBillCount\":501,\"missingStatementFactCount\":0,\"missingDueDateCount\":0,\"currencyMismatchCount\":0,\"invalidStatementAmountCount\":0,\"priorCurrencyConflictCount\":0}")]
    [InlineData("{\"eligibleBillCount\":499,\"missingStatementFactCount\":2,\"missingDueDateCount\":0,\"currencyMismatchCount\":0,\"invalidStatementAmountCount\":0,\"priorCurrencyConflictCount\":0}")]
    public async Task InvalidCoverageRefresh_PreservesLastVerifiedPaydayHistory(string invalid)
    {
        var module = new Module(); var normal = module.Reply;
        var valid = "{\"eligibleBillCount\":1,\"missingStatementFactCount\":0,\"missingDueDateCount\":0,\"currencyMismatchCount\":0,\"invalidStatementAmountCount\":0,\"priorCurrencyConflictCount\":0}";
        module.Reply = (name, args) => name == "getRecentPaydayPlans"
            ? Task.FromResult<string?>(History(valid)) : normal(name, args);
        var page = Page(module);
        await Run(page, "LoadAsync");
        Assert.Empty(Get<HashSet<string>>(page, "_loadErrors"));
        var oldPlan = Assert.Single(Get<IList>(page, "_recentPlans").Cast<object>());

        module.Reply = (name, args) => name == "getRecentPaydayPlans"
            ? Task.FromResult<string?>(History(invalid)) : normal(name, args);
        await Run(page, "RetryRecentAsync");
        Assert.Contains("recent", Get<HashSet<string>>(page, "_loadErrors"));
        Assert.Same(oldPlan, Assert.Single(Get<IList>(page, "_recentPlans").Cast<object>()));
        await page.DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BillAmount_KeepsTransactionEvidenceDistinctFromZero(bool evidenced)
    {
        var module = new Module();
        var normal = module.Reply;
        var evidenceJson = evidenced ? "true" : "false";
        module.Reply = (name, args) => name == "getBillStreams"
            ? Task.FromResult<string?>(Bills.Replace("\"hasTransactionEvidence\":true",
                "\"hasTransactionEvidence\":" + evidenceJson).Replace("89.25", "0"))
            : normal(name, args);
        var page = Page(module);
        await Run(page, "LoadAsync");
        var row = Bill(page);
        Assert.Equal(0m, Get<decimal>(row, "CurrentAmount"));
        Assert.Equal(evidenced, Get<bool>(row, "HasTransactionEvidence"));
        Assert.Empty(Get<HashSet<string>>(page, "_loadErrors"));
        Assert.DoesNotContain(module.Calls, call => call.Name.StartsWith("put") || call.Name.StartsWith("delete"));
        await page.DisposeAsync();
    }

    [Fact]
    public async Task MissingBillResponse_DoesNotBecomeNoActiveBills()
    {
        var module = new Module(); var normal = module.Reply;
        module.Reply = (name, args) => name == "getBillStreams" ? Task.FromResult<string?>(null) : normal(name, args);
        var page = Page(module); await Run(page, "LoadAsync");
        Assert.Contains("bills", Get<HashSet<string>>(page, "_loadErrors"));
    }
}

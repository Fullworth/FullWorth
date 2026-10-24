using System.Reflection;
using System.Text.Json;
using FullWorth.Web.Components.Pages.App;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class BacklogPageRecoveryTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [Theory]
    [InlineData("users", "_usersSkip")]
    [InlineData("keys", "_keysSkip")]
    [InlineData("audit", "_auditSkip")]
    public async Task AdminPagesAdvanceWithBoundedReadsAndFailedPagePreservesOffset(string dataset, string offset)
    {
        var page = new Admin();
        var module = new Module();
        Set(page, "_adminModule", module);
        Set(page, "_isLoading", false);
        Property(page, "L", new Localizer<Admin>());
        await Run(page, "ChangePageAsync", dataset, 1);
        Assert.Equal(50, Get<int>(page, offset));
        Assert.Equal(50, module.LastArguments![0]);
        Assert.Equal(50, module.LastArguments[1]);
        module.Fail = true;
        await Run(page, "ChangePageAsync", dataset, 1);
        Assert.Equal(50, Get<int>(page, offset));
        Assert.NotNull(Get<string?>(page, "_pagingError"));
        module.Fail = false;
        await Run(page, "ChangePageAsync", dataset, -1);
        Assert.Equal(0, Get<int>(page, offset));
        Assert.Null(Get<string?>(page, "_pagingError"));
        Assert.All(module.Calls, name => Assert.StartsWith("getAdmin", name));
    }

    private static Task Run(object page, string method, params object[] args) =>
        (Task)page.GetType().GetMethod(method, Flags)!.Invoke(page, args)!;
    private static void Set(object page, string name, object? value) => page.GetType().GetField(name, Flags)!.SetValue(page, value);
    private static void Property(object page, string name, object value) => page.GetType().GetProperty(name, Flags)!.SetValue(page, value);
    private static T Get<T>(object page, string name) => (T)page.GetType().GetField(name, Flags)!.GetValue(page)!;
    private sealed class Localizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] args] => new(name, string.Format(name, args));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
    private sealed class Module : IJSObjectReference
    {
        public bool Fail;
        public List<string> Calls = [];
        public object?[]? LastArguments;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args)
        {
            Calls.Add(identifier); LastArguments = args;
            if (Fail) throw new JSException("synthetic read failure");
            object value = identifier == "getAdminUsers" ? new[] { new { id = Guid.NewGuid(), email = "synthetic@example.test" } } :
                new { skip = (int)args![0]!, take = 50, totalCount = 151, items = new[] { new { id = Guid.NewGuid() } } };
            if (identifier.StartsWith("getAdmin", StringComparison.Ordinal))
                value = new { accessDenied = false, value };
            return ValueTask.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

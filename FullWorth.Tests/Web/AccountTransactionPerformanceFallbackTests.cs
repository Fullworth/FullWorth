using System.Reflection;
using FullWorth.Web.Components.Pages.App;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class AccountTransactionPerformanceFallbackTests
{
    private const BindingFlags Flags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class Runtime : IJSRuntime
    {
        public bool ThrowForProfile { get; set; }
        public bool MissingExecutionMode { get; set; }
        public int Invocations { get; private set; }

        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            InvokeAsync<T>(identifier, CancellationToken.None, args);

        public ValueTask<T> InvokeAsync<T>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Invocations++;
            if (ThrowForProfile ||
                (MissingExecutionMode && identifier.Contains("getExecutionMode")))
                return ValueTask.FromException<T>(new JSException("Optional script unavailable"));

            object value = identifier.Contains("getTransactionLoadLimit") ? 250 : "local";
            return ValueTask.FromResult((T)value);
        }
    }

    private static async Task<AccountTransactions> Initialize(Runtime runtime)
    {
        var page = new AccountTransactions();
        typeof(AccountTransactions).GetProperty("JS", Flags)!.SetValue(page, runtime);
        await (Task)typeof(AccountTransactions)
            .GetMethod("EnsureInitialTransactionLoadLimitAsync", Flags)!
            .Invoke(page, null)!;
        return page;
    }

    private static T Field<T>(AccountTransactions page, string key) =>
        (T)typeof(AccountTransactions).GetField(key, Flags)!.GetValue(page)!;

    [Fact]
    public async Task MissingProfilerUsesBoundedServerFallback()
    {
        var runtime = new Runtime { ThrowForProfile = true };
        var page = await Initialize(runtime);
        Assert.Equal(100, Field<int>(page, "_transactionLoadLimit"));
        Assert.False(Field<bool>(page, "_useLocalTransactionFiltering"));
        Assert.True(Field<bool>(page, "_hasResolvedInitialTransactionLoadLimit"));
    }

    [Fact]
    public async Task ProfileModeFailureDoesNotKeepPartialLocalState()
    {
        var runtime = new Runtime { MissingExecutionMode = true };
        var page = await Initialize(runtime);
        Assert.Equal(100, Field<int>(page, "_transactionLoadLimit"));
        Assert.False(Field<bool>(page, "_useLocalTransactionFiltering"));
    }

    [Fact]
    public async Task HealthyProfilePreservesDevicePreferenceWithoutDuplicateReads()
    {
        var runtime = new Runtime();
        var page = await Initialize(runtime);
        Assert.Equal(250, Field<int>(page, "_transactionLoadLimit"));
        Assert.True(Field<bool>(page, "_useLocalTransactionFiltering"));
        await (Task)typeof(AccountTransactions)
            .GetMethod("EnsureInitialTransactionLoadLimitAsync", Flags)!
            .Invoke(page, null)!;
        Assert.Equal(2, runtime.Invocations);
    }
}

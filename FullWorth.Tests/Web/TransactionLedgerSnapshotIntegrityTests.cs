using System.Reflection;
using System.Text.Json;
using FullWorth.Web.Components.Pages.App;
using Microsoft.JSInterop;

namespace FullWorth.Tests.Web;

public sealed class TransactionLedgerSnapshotIntegrityTests
{
    private const BindingFlags Flags =
        BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static AccountTransactions.BankTransactionViewModel Row() => new()
    {
        Id = Guid.NewGuid(),
        BankAccountId = Guid.NewGuid(),
        Name = "Synthetic posted charge",
        InstitutionName = "Synthetic test bank",
        AccountName = "Test checking",
        Amount = 24.95m,
        IsoCurrencyCode = "USD",
        PostedDate = new DateOnly(2026, 10, 15)
    };

    private static bool IsValid(AccountTransactions.BankTransactionViewModel[]? rows, int limit) =>
        (bool)typeof(AccountTransactions)
            .GetMethod("IsValidLedgerSnapshot", Flags)!
            .Invoke(null, [rows, limit])!;

    private static void Set(AccountTransactions component, string name, object value) =>
        typeof(AccountTransactions).GetField(name, Flags)!.SetValue(component, value);

    private static List<AccountTransactions.BankTransactionViewModel> Rows(AccountTransactions component) =>
        (List<AccountTransactions.BankTransactionViewModel>)typeof(AccountTransactions)
            .GetField("_transactions", Flags)!.GetValue(component)!;

    [Fact]
    public void SnapshotValidationRejectsMalformedAndDuplicateFinancialRows()
    {
        var good = Row();
        Assert.True(IsValid([], 100));
        Assert.True(IsValid([good], 100));
        Assert.False(IsValid(null, 100));
        Assert.False(IsValid([good], 0));
        Assert.False(IsValid([good, good], 100));
        Assert.False(IsValid([good], 0));
        Assert.False(IsValid([new AccountTransactions.BankTransactionViewModel()], 100));

        var invalidAccount = Row();
        invalidAccount.BankAccountId = Guid.Empty;
        Assert.False(IsValid([invalidAccount], 100));
        var invalidDate = Row();
        invalidDate.PostedDate = default;
        Assert.False(IsValid([invalidDate], 100));
        Assert.False(IsValid([Row(), Row()], 1));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("{}")]
    public async Task MalformedStreamDoesNotReplaceLastVerifiedLedger(string json)
    {
        var previous = Row();
        var component = new AccountTransactions();
        var module = new FakeModule(json);
        Set(component, "_bffModule", module);
        Set(component, "_transactionStreamModule", module);
        Set(component, "_transactionLoadLimit", 100);
        Set(component, "_transactions",
            new List<AccountTransactions.BankTransactionViewModel> { previous });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
            await (Task)typeof(AccountTransactions)
                .GetMethod("LoadTransactionsCoreAsync", Flags)!
                .Invoke(component, null)!);

        Assert.Same(previous, Assert.Single(Rows(component)));
        Assert.Equal(1, module.Reads);
        await component.DisposeAsync();
    }

    [Fact]
    public async Task GenuineEmptyStreamCanReplacePreviousSnapshot()
    {
        var component = new AccountTransactions();
        var module = new FakeModule("[]");
        Set(component, "_bffModule", module);
        Set(component, "_transactionStreamModule", module);
        Set(component, "_transactionLoadLimit", 100);
        Set(component, "_transactions",
            new List<AccountTransactions.BankTransactionViewModel> { Row() });

        await (Task)typeof(AccountTransactions)
            .GetMethod("LoadTransactionsCoreAsync", Flags)!
            .Invoke(component, null)!;

        Assert.Empty(Rows(component));
        Assert.Equal(1, module.Reads);
        await component.DisposeAsync();
    }

    [Fact]
    public async Task ValidSnapshotReplacesOldRowsWithoutChangingAmount()
    {
        var newRow = Row();
        var json = JsonSerializer.Serialize(new[] { newRow });
        var component = new AccountTransactions();
        var module = new FakeModule(json);
        Set(component, "_bffModule", module);
        Set(component, "_transactionStreamModule", module);
        Set(component, "_transactionLoadLimit", 100);
        Set(component, "_transactions",
            new List<AccountTransactions.BankTransactionViewModel> { Row() });

        await (Task)typeof(AccountTransactions)
            .GetMethod("LoadTransactionsCoreAsync", Flags)!
            .Invoke(component, null)!;

        var restored = Assert.Single(Rows(component));
        Assert.Equal(newRow.Id, restored.Id);
        Assert.Equal(24.95m, restored.Amount);
        Assert.Equal(newRow.PostedDate, restored.PostedDate);
        await component.DisposeAsync();
    }

    private sealed class FakeModule(string payload) : IJSObjectReference
    {
        public int Reads { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) =>
            InvokeAsync<T>(identifier, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(
            string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "deactivateLocalTransactionFiltering")
                return ValueTask.FromResult(default(T)!);
            if (identifier != "getBankTransactionsStream")
                throw new InvalidOperationException("Unexpected call");
            Reads++;
            return ValueTask.FromResult((T)(object)new FakeStream(payload));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeStream(string payload) : IJSStreamReference
    {
        public long Length => System.Text.Encoding.UTF8.GetByteCount(payload);
        public ValueTask<Stream> OpenReadStreamAsync(
            long maxAllowedSize = 512000, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(
                new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload)));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using System.Reflection;
using FullWorth.Web.Components.Pages.App;
using Microsoft.Extensions.Localization;

namespace FullWorth.Tests.Web;

public sealed class AccountTransactionAccountFilterRecoveryTests
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed class Localizer : IStringLocalizer<AccountTransactions>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] args] =>
            new(name, string.Format(name, args));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }

    private static void Set(AccountTransactions page, string field, object value) =>
        typeof(AccountTransactions).GetField(field, Flags)!.SetValue(page, value);
    private static T Get<T>(AccountTransactions page, string field) =>
        (T)typeof(AccountTransactions).GetField(field, Flags)!.GetValue(page)!;
    private static void Update(AccountTransactions page) =>
        typeof(AccountTransactions).GetMethod("UpdateTransactionSummaries", Flags)!
            .Invoke(page, null);

    private static AccountTransactions Page()
    {
        var page = new AccountTransactions();
        typeof(AccountTransactions).GetProperty("L", Flags)!
            .SetValue(page, new Localizer());
        return page;
    }

    private static AccountTransactions.BankTransactionViewModel Tx(Guid bankId) =>
        new()
        {
            Id = Guid.NewGuid(), BankAccountId = bankId,
            InstitutionName = "Synthetic bank", AccountName = "Synthetic checking",
            Amount = 25m, IsoCurrencyCode = "USD",
            Name = "Synthetic charge"
        };

    [Fact]
    public void DisappearingAccountResetsOnlyAccountFilter()
    {
        var page = Page();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Set(page, "_selectedAccountId", a.ToString("D"));
        Set(page, "_searchText", "synthetic");
        Set(page, "_transactions", new List<AccountTransactions.BankTransactionViewModel> { Tx(b) });

        Update(page);

        Assert.Equal(string.Empty, Get<string>(page, "_selectedAccountId"));
        Assert.Equal("synthetic", Get<string>(page, "_searchText"));
        Assert.NotNull(Get<string?>(page, "_accountFilterResetNotice"));
    }

    [Fact]
    public void RetainedAccountPreservesFilterAndDoesNotWarn()
    {
        var page = Page();
        var a = Guid.NewGuid();
        Set(page, "_selectedAccountId", a.ToString("D"));
        Set(page, "_transactions", new List<AccountTransactions.BankTransactionViewModel> { Tx(a) });

        Update(page);

        Assert.Equal(a.ToString("D"), Get<string>(page, "_selectedAccountId"));
        Assert.Null(Get<string?>(page, "_accountFilterResetNotice"));
    }

    [Fact]
    public void EmptySnapshotClearsNowInvisibleAccountFilter()
    {
        var page = Page();
        Set(page, "_selectedAccountId", Guid.NewGuid().ToString("D"));
        Set(page, "_transactions", new List<AccountTransactions.BankTransactionViewModel>());
        Update(page);
        Assert.Equal(string.Empty, Get<string>(page, "_selectedAccountId"));
        Assert.NotNull(Get<string?>(page, "_accountFilterResetNotice"));
    }
}

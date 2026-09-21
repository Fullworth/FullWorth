using System.Text.RegularExpressions;

namespace FullWorth.Tests.Security;

public sealed class WebTransactionPerformanceBoundaryTests
{
    [Fact]
    public void PerformanceProfile_ReducesInitialWindowWithoutRemovingSupportedHistoryAccess()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var transactionsPage =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "Components",
                    "Pages",
                    "App",
                    "AccountTransactions.razor"));

        Assert.Matches(
            @"ExpandedTransactionLoadLimit\s*=\s*250;",
            transactionsPage);

        Assert.Matches(
            @"MaximumTransactionLoadLimit\s*=\s*500;",
            transactionsPage);

        Assert.Contains(
            "private async Task LoadMoreAsync()",
            transactionsPage,
            StringComparison.Ordinal);

        Assert.Contains(
            "GetNextTransactionLoadLimit()",
            transactionsPage,
            StringComparison.Ordinal);

        Assert.Contains(
            "_hasResolvedInitialTransactionLoadLimit",
            transactionsPage,
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            Regex.Matches(
                    transactionsPage,
                    "FullWorthPerformance\\.getTransactionLoadLimit",
                    RegexOptions.CultureInvariant)
                .Count);

        Assert.Matches(
            @"_transactionLoadLimit\s*=\s*previousLimit;",
            transactionsPage);
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

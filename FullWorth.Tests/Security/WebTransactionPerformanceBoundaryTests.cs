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

        Assert.Contains(
            """
            private const int ExpandedTransactionLoadLimit =
                    250;
            """,
            transactionsPage,
            StringComparison.Ordinal);

        Assert.Contains(
            """
            private const int MaximumTransactionLoadLimit =
                    500;
            """,
            transactionsPage,
            StringComparison.Ordinal);

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

        Assert.Contains(
            """
            _transactionLoadLimit =
                    previousLimit;
            """,
            transactionsPage,
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

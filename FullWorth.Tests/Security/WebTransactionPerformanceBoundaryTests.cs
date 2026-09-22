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


    [Fact]
    public void AdaptiveExecution_UsesBrowserWorkerOnlyForHighCapabilityFiltering()
    {
        var repositoryRoot =
            FindRepositoryRoot();

        var performanceProfile =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "performance-profile.js"));

        var transactionStream =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "transaction-stream.js"));

        var transactionWorker =
            File.ReadAllText(
                Path.Combine(
                    repositoryRoot,
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "transaction-filter-worker.js"));

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
            "getExecutionModeForProfile",
            performanceProfile,
            StringComparison.Ordinal);

        Assert.Matches(
            @"case\s+""high"":\s*return\s+""local"";",
            performanceProfile);

        Assert.Matches(
            @"case\s+""balanced"":\s*return\s+""hybrid"";",
            performanceProfile);

        Assert.Contains(
            "return \"server\";",
            performanceProfile,
            StringComparison.Ordinal);

        Assert.Contains(
            "new Worker(",
            transactionStream,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"./transaction-filter-worker.js\"",
            transactionStream,
            StringComparison.Ordinal);

        Assert.Contains(
            "getExecutionMode() !== \"local\"",
            transactionStream,
            StringComparison.Ordinal);

        Assert.Contains(
            "activateLocalTransactionFiltering",
            transactionsPage,
            StringComparison.Ordinal);

        Assert.Contains(
            "data-fullworth-transaction-id",
            transactionsPage,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"filter\"",
            transactionWorker,
            StringComparison.Ordinal);

        foreach (var script
            in new[]
            {
                transactionStream,
                transactionWorker
            })
        {
            Assert.DoesNotContain(
                "localStorage",
                script,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "sessionStorage",
                script,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "indexedDB",
                script,
                StringComparison.OrdinalIgnoreCase);

            Assert.DoesNotContain(
                "caches.",
                script,
                StringComparison.OrdinalIgnoreCase);
        }
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

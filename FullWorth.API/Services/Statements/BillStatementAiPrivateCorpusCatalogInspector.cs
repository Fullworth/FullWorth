namespace FullWorth.API.Services.Statements;

/*
 * Offline-only preflight for a private statement corpus.
 *
 * Every discovered case is loaded through the bounded corpus loader.
 * InspectAsync returns aggregate coverage only; InspectAndSelectAsync also
 * carries validated cases in memory for the offline runner.
 * Neither method calls an AI provider or is registered in the API runtime.
 */
public sealed class BillStatementAiPrivateCorpusCatalogInspector
{
    private const int MaxCorpusCases =
        1_000;

    private readonly BillStatementAiPrivateCorpusLoader _loader;

    public BillStatementAiPrivateCorpusCatalogInspector(
        BillStatementAiPrivateCorpusLoader loader)
    {
        ArgumentNullException.ThrowIfNull(
            loader);

        _loader =
            loader;
    }

    public async Task<BillStatementAiPrivateCorpusCatalogSummary> InspectAsync(
        string corpusRootDirectory,
        CancellationToken cancellationToken = default)
    {
        var selection =
            await InspectAndSelectAsync(
                corpusRootDirectory,
                cancellationToken);

        return selection.Summary;
    }

    /*
     * The cases and identifiers stay in memory for the offline runner. They
     * must never be included in its aggregate report or application logs.
     */
    public async Task<BillStatementAiPrivateCorpusCatalogSelection> InspectAndSelectAsync(
        string corpusRootDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            corpusRootDirectory);

        if (!Path.IsPathFullyQualified(
                corpusRootDirectory))
        {
            throw new ArgumentException(
                "The private corpus root must be an absolute path.",
                nameof(corpusRootDirectory));
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await InspectCoreAsync(
                Path.GetFullPath(
                    corpusRootDirectory),
                cancellationToken);
        }
        catch (BillStatementAiPrivateCorpusException)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (IOException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus catalog could not be inspected.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus catalog could not be inspected.");
        }
    }

    private async Task<BillStatementAiPrivateCorpusCatalogSelection>
        InspectCoreAsync(
            string corpusRootDirectory,
            CancellationToken cancellationToken)
    {
        if (!Directory.Exists(
                corpusRootDirectory))
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus root directory is missing.");
        }

        var rootAttributes =
            File.GetAttributes(
                corpusRootDirectory);

        if ((rootAttributes &
                FileAttributes.ReparsePoint) !=
            0)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus root cannot be a link or reparse point.");
        }

        var caseDirectories =
            Directory.EnumerateDirectories(
                    corpusRootDirectory,
                    "*",
                    SearchOption.TopDirectoryOnly)
                .Take(
                    MaxCorpusCases +
                    1)
                .ToArray();

        if (caseDirectories.Length ==
            0)
        {
            throw new BillStatementAiPrivateCorpusException(
                "The private corpus contains no cases.");
        }

        if (caseDirectories.Length >
            MaxCorpusCases)
        {
            throw new BillStatementAiPrivateCorpusException(
                $"The private corpus contains more than {MaxCorpusCases} cases.");
        }

        var caseIds =
            new List<string>(
                caseDirectories.Length);

        var seenCaseIds =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var caseDirectory in
                 caseDirectories)
        {
            var attributes =
                File.GetAttributes(
                    caseDirectory);

            if ((attributes &
                    FileAttributes.ReparsePoint) !=
                0)
            {
                throw new BillStatementAiPrivateCorpusException(
                    "A private corpus case directory cannot be a link or reparse point.");
            }

            var caseId =
                Path.GetFileName(
                    Path.TrimEndingDirectorySeparator(
                        caseDirectory));

            /*
             * Resolve through the existing policy before any case file read.
             * This rejects unsafe names and path traversal consistently.
             */
            BillStatementAiPrivateCorpusPathPolicy
                .ResolveStatementTextPath(
                    corpusRootDirectory,
                    caseId);

            if (!seenCaseIds.Add(
                    caseId))
            {
                throw new BillStatementAiPrivateCorpusException(
                    "The private corpus contains duplicate case identifiers.");
            }

            caseIds.Add(
                caseId);
        }

        caseIds.Sort(
            StringComparer.Ordinal);

        var providerCounts =
            new Dictionary<string, long>(
                StringComparer.Ordinal);

        var cases =
            new List<BillStatementAiPrivateCorpusCase>(
                caseIds.Count);

        foreach (var caseId in
                 caseIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var corpusCase =
                await _loader.LoadAsync(
                    corpusRootDirectory,
                    caseId,
                    cancellationToken);

            cases.Add(
                corpusCase);

            var providerKey =
                corpusCase.ProviderKey
                    .Trim()
                    .ToUpperInvariant();

            providerCounts[providerKey] =
                providerCounts.GetValueOrDefault(
                    providerKey) +
                1;
        }

        return new BillStatementAiPrivateCorpusCatalogSelection(
            new BillStatementAiPrivateCorpusCatalogSummary(
                CaseCount:
                    caseIds.Count,
                DistinctProviderCount:
                    providerCounts.Count,
                MinimumCasesForAnyProvider:
                    providerCounts.Values.Min()),
            caseIds,
            new BillStatementAiPrivateCorpusSnapshot(
                cases));
    }
}

public sealed record BillStatementAiPrivateCorpusCatalogSummary(
    long CaseCount,
    long DistinctProviderCount,
    long MinimumCasesForAnyProvider);

public sealed class BillStatementAiPrivateCorpusCatalogSelection
{
    internal BillStatementAiPrivateCorpusCatalogSelection(
        BillStatementAiPrivateCorpusCatalogSummary summary,
        List<string> caseIds,
        BillStatementAiPrivateCorpusSnapshot snapshot)
    {
        Summary =
            summary;

        CaseIds =
            caseIds.AsReadOnly();

        Snapshot =
            snapshot;
    }

    public BillStatementAiPrivateCorpusCatalogSummary Summary { get; }

    public IReadOnlyList<string> CaseIds { get; }

    public BillStatementAiPrivateCorpusSnapshot Snapshot { get; }
}

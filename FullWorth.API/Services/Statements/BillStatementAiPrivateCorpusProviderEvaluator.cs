namespace FullWorth.API.Services.Statements;

/*
 * Offline-only provider evaluator for an explicitly approved private
 * statement corpus.
 *
 * This class is intentionally NOT registered in Program.
 *
 * Safety properties:
 *
 * - requires explicit authorization before any provider call
 * - validates every selected corpus case before any provider call
 * - requires the existing aggregate coverage gate to pass first
 * - makes one extractor/provider attempt per selected case in this evaluator mode
 * - reports aggregate inference-call count separately from statement attempts
 * - validates model output through FullWorth's deterministic trust boundary
 * - treats rejected AI candidates as unusable rather than trusted facts
 * - stores nothing
 * - logs nothing
 * - returns aggregate metrics only
 * - never authorizes runtime shadow mode
 * - never authorizes AI-derived persistence
 *
 * Provider keys, statement text, expected facts, model output, evidence,
 * account information, and case identifiers never appear in the result.
 */
public sealed class BillStatementAiPrivateCorpusProviderEvaluator
{
    private const int MaxCasesPerRun =
        1_000;

    private readonly BillStatementAiPrivateCorpusLoader
        _loader;

    private readonly BillStatementAiPrivateCorpusCoverageGate
        _coverageGate;

    private readonly IBillStatementAiExtractor
        _aiExtractor;

    private readonly BillStatementAiCandidateConversionService
        _conversionService;

    private readonly BillStatementAiGroundTruthScorer
        _groundTruthScorer;

    private readonly BillStatementAiChunkedExtractionCoordinator
        _chunkedExtractionCoordinator;

    public BillStatementAiPrivateCorpusProviderEvaluator(
        BillStatementAiPrivateCorpusLoader loader,
        BillStatementAiPrivateCorpusCoverageGate coverageGate,
        IBillStatementAiExtractor aiExtractor,
        BillStatementAiCandidateConversionService conversionService,
        BillStatementAiGroundTruthScorer groundTruthScorer)
    {
        ArgumentNullException.ThrowIfNull(
            loader);

        ArgumentNullException.ThrowIfNull(
            coverageGate);

        ArgumentNullException.ThrowIfNull(
            aiExtractor);

        ArgumentNullException.ThrowIfNull(
            conversionService);

        ArgumentNullException.ThrowIfNull(
            groundTruthScorer);

        _loader =
            loader;

        _coverageGate =
            coverageGate;

        _aiExtractor =
            aiExtractor;

        _conversionService =
            conversionService;

        _groundTruthScorer =
            groundTruthScorer;

        _chunkedExtractionCoordinator =
            new BillStatementAiChunkedExtractionCoordinator(
                _aiExtractor,
                new BillStatementAiDocumentChunker(),
                new BillStatementAiChunkCandidateReconciler(
                    new BillStatementAiCandidateValidator()));
    }

    public async Task<BillStatementAiPrivateCorpusProviderEvaluationResult>
        EvaluateAsync(
            string corpusRootDirectory,
            IReadOnlyList<string> caseIds,
            string promptVersion,
            bool providerCallsAuthorized,
            BillStatementAiShadowReadinessPolicy readinessPolicy,
            CancellationToken cancellationToken = default,
            int? maxCharactersPerInference = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            corpusRootDirectory);

        ArgumentNullException.ThrowIfNull(
            caseIds);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            promptVersion);

        ArgumentNullException.ThrowIfNull(
            readinessPolicy);

        if (maxCharactersPerInference is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharactersPerInference),
                maxCharactersPerInference,
                "Maximum characters per inference must be positive when configured.");
        }

        ValidateCaseIds(
            caseIds);

        /*
         * Provider spend must be explicitly authorized.
         *
         * Do this before reading the private corpus so a caller cannot use
         * this evaluator as an accidental corpus reader when provider
         * evaluation was not actually approved.
         */
        if (!providerCallsAuthorized)
        {
            throw new InvalidOperationException(
                "Offline AI provider evaluation requires explicit provider-call authorization.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Load and validate the entire selected population before the first
         * provider call.
         *
         * This prevents a malformed case discovered halfway through a run
         * from consuming provider spend for the cases that happened to come
         * before it.
         */
        var corpusCases =
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

            corpusCases.Add(
                corpusCase);
        }

        var coverageSummary =
            CreateCoverageSummary(
                corpusCases);

        var coverageDecision =
            _coverageGate.Evaluate(
                coverageSummary,
                readinessPolicy);

        /*
         * Fail closed before any provider call.
         *
         * Coverage failure is a valid evaluation outcome, not an exception.
         * The caller receives aggregate-only reasons from the existing
         * coverage gate and can improve the corpus before spending money.
         */
        if (!coverageDecision
                .MayBeginOfflineProviderEvaluation)
        {
            return
                BillStatementAiPrivateCorpusProviderEvaluationResult
                    .CoverageRejected(
                        coverageSummary,
                        coverageDecision);
        }

        var observations =
            new List<BillStatementAiGroundTruthObservation>(
                corpusCases.Count);

        var providerAttemptDurationsMilliseconds =
            new List<double>(
                corpusCases.Count);

        var providerFailureKinds =
            new Dictionary<
                BillStatementAiExtractionFailureKind,
                long>();

        var inferenceCallCounter =
            _aiExtractor as
                IBillStatementAiInferenceCallCounter;

        var inferenceCallBaseline =
            inferenceCallCounter?
                .InferenceCallCount ??
            0L;

        long extractorAttemptCount =
            0;

        long multiInferenceStatementCount =
            0;

        long maximumInferenceCallsPerStatement =
            0;

        long chunkedExtractionRejectedStatementCount =
            0;

        foreach (var corpusCase in
                 corpusCases)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BillStatementExtractionResult? extraction =
                null;

            var providerFailed =
                false;

            var providerAttemptStarted =
                global::System.Diagnostics.Stopwatch
                    .GetTimestamp();

            var caseInferenceBaseline =
                inferenceCallCounter?
                    .InferenceCallCount ??
                0L;

            var caseExtractorAttemptBaseline =
                extractorAttemptCount;

            try
            {
                /*
                 * Do not provide the ground-truth provider identity as a hint.
                 *
                 * The purpose of this evaluation is to measure the extractor,
                 * not help it by leaking approved answers into its context.
                 */
                extractorAttemptCount =
                    checked(
                        extractorAttemptCount +
                        1);

                var request =
                    new BillStatementAiExtractionRequest(
                        DocumentText:
                            corpusCase.StatementText,

                        Hints:
                            new BillStatementExtractionHints(
                                ExpectedProviderName:
                                    null,

                                ExpectedCategory:
                                    null),

                        PromptVersion:
                            promptVersion);

                BillStatementAiCandidate? candidate;

                if (maxCharactersPerInference.HasValue)
                {
                    var chunkedResult =
                        await _chunkedExtractionCoordinator
                            .ExtractAsync(
                                request,
                                maxCharactersPerInference.Value,
                                cancellationToken);

                    if (!chunkedResult.IsAccepted)
                    {
                        chunkedExtractionRejectedStatementCount =
                            checked(
                                chunkedExtractionRejectedStatementCount +
                                1);
                    }

                    candidate =
                        chunkedResult.IsAccepted
                            ? chunkedResult.Candidate
                            : null;
                }
                else
                {
                    candidate =
                        await _aiExtractor.ExtractAsync(
                            request,
                            cancellationToken);
                }

                /*
                 * A provider response is still untrusted candidate data.
                 *
                 * It must pass FullWorth's existing deterministic evidence
                 * and candidate validation boundary before it may count as
                 * an extraction result in the evaluation.
                 *
                 * Deterministic chunk reconciliation rejection is deliberately
                 * treated like any other rejected candidate: missed truth,
                 * not a provider transport failure.
                 */
                if (candidate is not null)
                {
                    var conversion =
                        _conversionService.Convert(
                            corpusCase.StatementText,
                            candidate);

                    if (conversion.IsAccepted)
                    {
                        extraction =
                            conversion.Extraction;
                    }
                }

                /*
                 * A rejected candidate is intentionally not classified as a
                 * provider transport failure.
                 *
                 * The provider did respond; FullWorth rejected its candidate.
                 * That should reduce recall/readiness instead of disguising
                 * the trust-boundary rejection as network instability.
                 */
            }
            catch (BillStatementAiExtractionException exception)
            {
                /*
                 * Provider-specific failure details are intentionally dropped.
                 *
                 * Only the coarse, vendor-neutral FailureKind is retained for
                 * aggregate diagnostics. Messages and inner exceptions never
                 * enter the evaluation result.
                 */
                providerFailed =
                    true;

                providerFailureKinds[
                    exception.FailureKind] =
                    providerFailureKinds.GetValueOrDefault(
                        exception.FailureKind) +
                    1;
            }
            finally
            {
                providerAttemptDurationsMilliseconds.Add(
                    global::System.Diagnostics.Stopwatch
                        .GetElapsedTime(
                            providerAttemptStarted)
                        .TotalMilliseconds);

                var caseInferenceCalls =
                    GetInferenceCallCount(
                        inferenceCallCounter,
                        caseInferenceBaseline,
                        extractorAttemptCount -
                            caseExtractorAttemptBaseline);

                if (caseInferenceCalls >
                    1)
                {
                    multiInferenceStatementCount =
                        checked(
                            multiInferenceStatementCount +
                            1);
                }

                maximumInferenceCallsPerStatement =
                    Math.Max(
                        maximumInferenceCallsPerStatement,
                        caseInferenceCalls);
            }

            observations.Add(
                new BillStatementAiGroundTruthObservation(
                    ProviderKey:
                        corpusCase.ProviderKey,

                    ExpectedStatement:
                        corpusCase.ExpectedStatement,

                    ExpectedLineItems:
                        corpusCase.ExpectedLineItems,

                    ProviderAttempted:
                        true,

                    ProviderFailed:
                        providerFailed,

                    ActualExtraction:
                        extraction,

                    /*
                     * False-alert measurement is deliberately not fabricated.
                     *
                     * A separate deterministic alert-evaluation checkpoint
                     * must establish this value before the overall shadow
                     * readiness gate can pass.
                     */
                    AlertEvaluated:
                        false,

                    FalseAlert:
                        false));
        }

        /*
         * Score while the sensitive observations are still in memory, then
         * return only aggregate counters.
         */
        var metrics =
            _groundTruthScorer.Score(
                observations);

        var fieldScores =
            _groundTruthScorer.ScoreFields(
                observations);

        var providerScores =
            _groundTruthScorer.ScoreProviders(
                observations);

        var providerFieldScores =
            _groundTruthScorer.ScoreProviderFields(
                observations);

        var inferenceCallCount =
            GetInferenceCallCount(
                inferenceCallCounter,
                inferenceCallBaseline,
                extractorAttemptCount);

        var providerAttemptLatency =
            BillStatementAiProviderAttemptLatencySummary
                .Create(
                    providerAttemptDurationsMilliseconds);

        var failureKindCounts =
            providerFailureKinds
                .OrderBy(
                    pair =>
                        pair.Key)
                .Select(
                    pair =>
                        new BillStatementAiExtractionFailureCount(
                            FailureKind:
                                pair.Key,

                            Count:
                                pair.Value))
                .ToArray();

        return
            BillStatementAiPrivateCorpusProviderEvaluationResult
                .Completed(
                    coverageSummary,
                    coverageDecision,
                    metrics,
                    fieldScores,
                    providerScores,
                    providerFieldScores,
                    inferenceCallCount,
                    multiInferenceStatementCount,
                    maximumInferenceCallsPerStatement,
                    chunkedExtractionRejectedStatementCount,
                    providerAttemptLatency,
                    failureKindCounts);
    }

    private static long GetInferenceCallCount(
        IBillStatementAiInferenceCallCounter? inferenceCallCounter,
        long inferenceCallBaseline,
        long extractorAttemptCount)
    {
        if (inferenceCallCounter is null)
        {
            return extractorAttemptCount;
        }

        var currentCount =
            inferenceCallCounter.InferenceCallCount;

        if (currentCount <
            inferenceCallBaseline)
        {
            throw new InvalidOperationException(
                "The aggregate inference-call counter moved backwards during evaluation.");
        }

        return checked(
            currentCount -
            inferenceCallBaseline);
    }

    private static void ValidateCaseIds(
        IReadOnlyList<string> caseIds)
    {
        if (caseIds.Count is <
                1 or >
                MaxCasesPerRun)
        {
            throw new ArgumentOutOfRangeException(
                nameof(caseIds),
                $"An offline provider evaluation requires between 1 and {MaxCasesPerRun} cases.");
        }

        if (caseIds.Any(
                string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Private corpus case identifiers cannot be empty.",
                nameof(caseIds));
        }

        if (caseIds.Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .Count() !=
            caseIds.Count)
        {
            throw new ArgumentException(
                "Private corpus case identifiers must be unique.",
                nameof(caseIds));
        }
    }

    private static BillStatementAiPrivateCorpusCatalogSummary
        CreateCoverageSummary(
            IReadOnlyList<BillStatementAiPrivateCorpusCase> corpusCases)
    {
        ArgumentNullException.ThrowIfNull(
            corpusCases);

        if (corpusCases.Count ==
            0)
        {
            throw new ArgumentException(
                "At least one validated private corpus case is required.",
                nameof(corpusCases));
        }

        var providerCounts =
            new Dictionary<string, long>(
                StringComparer.Ordinal);

        foreach (var corpusCase in
                 corpusCases)
        {
            ArgumentNullException.ThrowIfNull(
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

        return new BillStatementAiPrivateCorpusCatalogSummary(
            CaseCount:
                corpusCases.Count,

            DistinctProviderCount:
                providerCounts.Count,

            MinimumCasesForAnyProvider:
                providerCounts.Values.Min());
    }
}

/*
 * Aggregate-only result.
 *
 * Nothing here can expose statement content, ground truth, model output,
 * provider identity, evidence excerpts, local paths, or case identifiers.
 */
public sealed record BillStatementAiPrivateCorpusProviderEvaluationResult(
    bool ProviderEvaluationStarted,
    BillStatementAiPrivateCorpusCatalogSummary Coverage,
    BillStatementAiPrivateCorpusCoverageDecision CoverageDecision,
    BillStatementAiShadowReadinessMetrics? Metrics,
    IReadOnlyList<BillStatementAiFieldScore>? FieldScores,
    IReadOnlyList<BillStatementAiProviderScore>? ProviderScores,
    IReadOnlyList<BillStatementAiProviderFieldScore>? ProviderFieldScores,
    long? InferenceCallCount,
    long? MultiInferenceStatementCount,
    long? MaximumInferenceCallsPerStatement,
    long? ChunkedExtractionRejectedStatementCount,
    BillStatementAiProviderAttemptLatencySummary? ProviderAttemptLatency,
    IReadOnlyList<BillStatementAiExtractionFailureCount>? FailureKindCounts)
{
    public bool MayEnableRuntimeShadowMode =>
        false;

    public bool MayInfluencePersistence =>
        false;

    public static BillStatementAiPrivateCorpusProviderEvaluationResult
        CoverageRejected(
            BillStatementAiPrivateCorpusCatalogSummary coverage,
            BillStatementAiPrivateCorpusCoverageDecision coverageDecision)
    {
        ArgumentNullException.ThrowIfNull(
            coverage);

        ArgumentNullException.ThrowIfNull(
            coverageDecision);

        return new BillStatementAiPrivateCorpusProviderEvaluationResult(
            ProviderEvaluationStarted:
                false,

            Coverage:
                coverage,

            CoverageDecision:
                coverageDecision,

            Metrics:
                null,

            FieldScores:
                null,

            ProviderScores:
                null,

            ProviderFieldScores:
                null,

            InferenceCallCount:
                null,

            MultiInferenceStatementCount:
                null,

            MaximumInferenceCallsPerStatement:
                null,

            ChunkedExtractionRejectedStatementCount:
                null,

            ProviderAttemptLatency:
                null,

            FailureKindCounts:
                null);
    }

    public static BillStatementAiPrivateCorpusProviderEvaluationResult
        Completed(
            BillStatementAiPrivateCorpusCatalogSummary coverage,
            BillStatementAiPrivateCorpusCoverageDecision coverageDecision,
            BillStatementAiShadowReadinessMetrics metrics,
            IReadOnlyList<BillStatementAiFieldScore> fieldScores,
            IReadOnlyList<BillStatementAiProviderScore> providerScores,
            IReadOnlyList<BillStatementAiProviderFieldScore> providerFieldScores,
            long inferenceCallCount,
            long multiInferenceStatementCount,
            long maximumInferenceCallsPerStatement,
            long chunkedExtractionRejectedStatementCount,
            BillStatementAiProviderAttemptLatencySummary providerAttemptLatency,
            IReadOnlyList<BillStatementAiExtractionFailureCount> failureKindCounts)
    {
        ArgumentNullException.ThrowIfNull(
            coverage);

        ArgumentNullException.ThrowIfNull(
            coverageDecision);

        ArgumentNullException.ThrowIfNull(
            metrics);

        ArgumentNullException.ThrowIfNull(
            fieldScores);

        ArgumentNullException.ThrowIfNull(
            providerScores);

        ArgumentNullException.ThrowIfNull(
            providerFieldScores);

        ArgumentNullException.ThrowIfNull(
            providerAttemptLatency);

        ArgumentNullException.ThrowIfNull(
            failureKindCounts);

        if (fieldScores.Count !=
            BillStatementAiGroundTruthFieldKeys.All.Count)
        {
            throw new ArgumentException(
                "A completed provider evaluation requires the fixed field-score set.",
                nameof(fieldScores));
        }

        if (!coverageDecision
                .MayBeginOfflineProviderEvaluation)
        {
            throw new ArgumentException(
                "A completed provider evaluation requires a passing corpus coverage decision.",
                nameof(coverageDecision));
        }

        if (providerScores.Count !=
            coverage.DistinctProviderCount)
        {
            throw new ArgumentException(
                "A completed provider evaluation requires one anonymous provider score per covered provider.",
                nameof(providerScores));
        }

        if (providerFieldScores.Count !=
            providerScores.Count)
        {
            throw new ArgumentException(
                "A completed provider evaluation requires field scores for every anonymous provider.",
                nameof(providerFieldScores));
        }

        var expectedOrdinal =
            1;

        foreach (var providerScore in
                 providerScores)
        {
            ArgumentNullException.ThrowIfNull(
                providerScore);

            if (providerScore.ProviderOrdinal !=
                    expectedOrdinal ||
                providerScore.StatementCount <=
                    0 ||
                providerScore.ProviderAttemptCount <
                    0 ||
                providerScore.ProviderFailureCount <
                    0 ||
                providerScore.ReadyCandidateStatementCount <
                    0 ||
                providerScore.CorrectFactCount <
                    0 ||
                providerScore.IncorrectFactCount <
                    0 ||
                providerScore.MissedFactCount <
                    0 ||
                providerScore.ProviderAttemptCount >
                    providerScore.StatementCount ||
                providerScore.ProviderFailureCount >
                    providerScore.ProviderAttemptCount ||
                providerScore.ReadyCandidateStatementCount >
                    providerScore.ProviderAttemptCount)
            {
                throw new ArgumentException(
                    "Completed provider evaluation contains an invalid anonymous provider score.",
                    nameof(providerScores));
            }

            expectedOrdinal++;
        }

        if (providerScores.Sum(
                    score =>
                        score.StatementCount) !=
                metrics.EvaluatedStatementCount ||
            providerScores.Sum(
                    score =>
                        score.ProviderAttemptCount) !=
                metrics.ProviderAttemptCount ||
            providerScores.Sum(
                    score =>
                        score.ProviderFailureCount) !=
                metrics.ProviderFailureCount ||
            providerScores.Sum(
                    score =>
                        score.ReadyCandidateStatementCount) !=
                metrics.ReadyCandidateStatementCount ||
            providerScores.Sum(
                    score =>
                        score.CorrectFactCount) !=
                metrics.CorrectFactCount ||
            providerScores.Sum(
                    score =>
                        score.IncorrectFactCount) !=
                metrics.IncorrectFactCount ||
            providerScores.Sum(
                    score =>
                        score.MissedFactCount) !=
                metrics.MissedFactCount)
        {
            throw new ArgumentException(
                "Anonymous provider scores do not reconcile with aggregate evaluation metrics.",
                nameof(providerScores));
        }

        if (inferenceCallCount <
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inferenceCallCount),
                "Inference-call count cannot be negative.");
        }

        if (multiInferenceStatementCount <
                0 ||
            multiInferenceStatementCount >
                metrics.ProviderAttemptCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(multiInferenceStatementCount),
                "Multi-inference statement count must fit within provider attempts.");
        }

        if (maximumInferenceCallsPerStatement <
                0 ||
            maximumInferenceCallsPerStatement >
                inferenceCallCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumInferenceCallsPerStatement),
                "Maximum inference calls per statement must fit within aggregate inference calls.");
        }

        if (chunkedExtractionRejectedStatementCount <
                0 ||
            chunkedExtractionRejectedStatementCount >
                metrics.ProviderAttemptCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(chunkedExtractionRejectedStatementCount),
                "Chunked extraction rejection count must fit within provider attempts.");
        }

        if (providerAttemptLatency.AttemptCount !=
                metrics.ProviderAttemptCount ||
            providerAttemptLatency.MinimumMilliseconds <
                0d ||
            providerAttemptLatency.MeanMilliseconds <
                providerAttemptLatency.MinimumMilliseconds ||
            providerAttemptLatency.P50Milliseconds <
                providerAttemptLatency.MinimumMilliseconds ||
            providerAttemptLatency.P95Milliseconds <
                providerAttemptLatency.P50Milliseconds ||
            providerAttemptLatency.MaximumMilliseconds <
                providerAttemptLatency.P95Milliseconds)
        {
            throw new ArgumentException(
                "Provider-attempt latency summary does not reconcile with the completed evaluation.",
                nameof(providerAttemptLatency));
        }

        var seenFailureKinds =
            new HashSet<
                BillStatementAiExtractionFailureKind>();

        long classifiedFailureCount =
            0;

        foreach (var failureKindCount in
                 failureKindCounts)
        {
            ArgumentNullException.ThrowIfNull(
                failureKindCount);

            if (!Enum.IsDefined(
                    failureKindCount.FailureKind) ||
                !seenFailureKinds.Add(
                    failureKindCount.FailureKind) ||
                failureKindCount.Count <=
                    0)
            {
                throw new ArgumentException(
                    "Provider failure-kind counts contain an invalid or duplicate category.",
                    nameof(failureKindCounts));
            }

            classifiedFailureCount +=
                failureKindCount.Count;
        }

        if (classifiedFailureCount !=
            metrics.ProviderFailureCount)
        {
            throw new ArgumentException(
                "Provider failure-kind counts do not reconcile with aggregate provider failures.",
                nameof(failureKindCounts));
        }

        return new BillStatementAiPrivateCorpusProviderEvaluationResult(
            ProviderEvaluationStarted:
                true,

            Coverage:
                coverage,

            CoverageDecision:
                coverageDecision,

            Metrics:
                metrics,

            FieldScores:
                fieldScores,

            ProviderScores:
                providerScores,

            ProviderFieldScores:
                providerFieldScores,

            InferenceCallCount:
                inferenceCallCount,

            MultiInferenceStatementCount:
                multiInferenceStatementCount,

            MaximumInferenceCallsPerStatement:
                maximumInferenceCallsPerStatement,

            ChunkedExtractionRejectedStatementCount:
                chunkedExtractionRejectedStatementCount,

            ProviderAttemptLatency:
                providerAttemptLatency,

            FailureKindCounts:
                failureKindCounts);
    }
}

public sealed record BillStatementAiExtractionFailureCount(
    BillStatementAiExtractionFailureKind FailureKind,
    long Count);

public sealed record BillStatementAiProviderAttemptLatencySummary(
    long AttemptCount,
    double MinimumMilliseconds,
    double MeanMilliseconds,
    double P50Milliseconds,
    double P95Milliseconds,
    double MaximumMilliseconds)
{
    public static BillStatementAiProviderAttemptLatencySummary Create(
        IReadOnlyList<double> durationsMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(
            durationsMilliseconds);

        if (durationsMilliseconds.Count ==
            0)
        {
            throw new ArgumentException(
                "At least one provider-attempt duration is required.",
                nameof(durationsMilliseconds));
        }

        if (durationsMilliseconds.Any(
                duration =>
                    duration <
                        0d ||
                    double.IsNaN(
                        duration) ||
                    double.IsInfinity(
                        duration)))
        {
            throw new ArgumentOutOfRangeException(
                nameof(durationsMilliseconds),
                "Provider-attempt durations must be finite and non-negative.");
        }

        var ordered =
            durationsMilliseconds
                .Order()
                .ToArray();

        return new BillStatementAiProviderAttemptLatencySummary(
            AttemptCount:
                ordered.LongLength,

            MinimumMilliseconds:
                ordered[0],

            MeanMilliseconds:
                ordered.Average(),

            P50Milliseconds:
                Percentile(
                    ordered,
                    0.50d),

            P95Milliseconds:
                Percentile(
                    ordered,
                    0.95d),

            MaximumMilliseconds:
                ordered[^1]);
    }

    private static double Percentile(
        IReadOnlyList<double> ordered,
        double percentile)
    {
        var rank =
            Math.Max(
                1,
                (int)Math.Ceiling(
                    ordered.Count *
                    percentile));

        return ordered[
            rank -
            1];
    }
}

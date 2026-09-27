using System.Diagnostics;
using System.Text.Json;
using FullWorth.API.Services.Statements;
using Microsoft.Extensions.Options;

internal static class Program
{
    private const string BaselineMode =
        "baseline";

    private const string LocalAiMode =
        "local-ai";

    private const string ComparePromptsMode =
        "compare-prompts";

    private const string ApprovedModelId =
        "qwen3-4b-q4-k-m";

    private const string ApprovedModelSha256 =
        "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5";

    private const string ApprovedRuntimeId =
        "llama-cpp-server-b11176";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            WriteIndented = true
        };

    private static async Task<int> Main(
        string[] args)
    {
        if (args.Length == 1 &&
            args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        if (!TryParseArguments(
                args,
                out var parsed))
        {
            PrintUsage();
            return 2;
        }

        if ((parsed.Mode is
                 LocalAiMode or
                 ComparePromptsMode) &&
            !parsed.LocalInferenceAuthorized)
        {
            Console.Error.WriteLine(
                "Local model evaluation requires --authorize-local-model-inference.");
            return 2;
        }

        try
        {
            var fullCorpusRoot =
                Path.GetFullPath(
                    parsed.CorpusRoot);

            var loader =
                new BillStatementAiPrivateCorpusLoader();

            var catalog =
                await new BillStatementAiPrivateCorpusCatalogInspector(
                    loader)
                    .InspectAsync(
                        fullCorpusRoot);

            var caseIds =
                Directory.GetDirectories(
                        fullCorpusRoot,
                        "*",
                        SearchOption.TopDirectoryOnly)
                    .Select(
                        directory =>
                            new DirectoryInfo(
                                directory).Name)
                    .Order(
                        StringComparer.Ordinal)
                    .ToArray();

            if (parsed.Mode ==
                BaselineMode)
            {
                return await RunDeterministicBaselineAsync(
                    loader,
                    catalog,
                    fullCorpusRoot,
                    caseIds);
            }

            var apiKey =
                Environment.GetEnvironmentVariable(
                    "FULLWORTH_LOCAL_AI_API_KEY");

            if (string.IsNullOrWhiteSpace(
                    apiKey))
            {
                Console.Error.WriteLine(
                    "FULLWORTH_LOCAL_AI_API_KEY must be provided through the process environment.");
                return 2;
            }

            using var httpClient =
                new HttpClient();

            var readinessPolicy =
                BillStatementAiShadowReadinessPolicy
                    .PrivateBetaDefault;

            if (parsed.Mode ==
                ComparePromptsMode)
            {
                return await RunPromptComparisonAsync(
                    loader,
                    httpClient,
                    fullCorpusRoot,
                    caseIds,
                    apiKey,
                    readinessPolicy);
            }

            return await RunSinglePromptEvaluationAsync(
                loader,
                httpClient,
                fullCorpusRoot,
                caseIds,
                apiKey,
                parsed.PromptVersion,
                readinessPolicy);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(
                "Evaluation canceled; sensitive details were suppressed.");
            return 1;
        }
        catch (Exception)
        {
            Console.Error.WriteLine(
                "Evaluation failed; sensitive details were suppressed.");
            return 1;
        }
    }

    private static async Task<int> RunDeterministicBaselineAsync(
        BillStatementAiPrivateCorpusLoader loader,
        BillStatementAiPrivateCorpusCatalogSummary catalog,
        string fullCorpusRoot,
        IReadOnlyList<string> caseIds)
    {
        var baseline =
            await new BillStatementDeterministicPrivateCorpusEvaluator(
                loader,
                new DeterministicBillStatementExtractionService(
                    new DeterministicBillStatementParser(),
                    new DeterministicBillLineItemParser()))
                .EvaluateAsync(
                    fullCorpusRoot,
                    caseIds);

        WriteJson(
            new
            {
                mode =
                    BaselineMode,

                catalog.CaseCount,
                catalog.DistinctProviderCount,
                catalog.MinimumCasesForAnyProvider,
                baseline.EvaluatedStatementCount,
                baseline.ReadyStatementCount,
                baseline.CorrectFactCount,
                baseline.IncorrectFactCount,
                baseline.MissedFactCount,
                baseline.ReadyStatementRate,
                baseline.FactPrecision,
                baseline.FactRecall,
                persisted =
                    false
            });

        return 0;
    }

    private static async Task<int> RunSinglePromptEvaluationAsync(
        BillStatementAiPrivateCorpusLoader loader,
        HttpClient httpClient,
        string fullCorpusRoot,
        IReadOnlyList<string> caseIds,
        string apiKey,
        string promptVersion,
        BillStatementAiShadowReadinessPolicy readinessPolicy)
    {
        var run =
            await EvaluatePromptAsync(
                loader,
                httpClient,
                fullCorpusRoot,
                caseIds,
                apiKey,
                promptVersion,
                readinessPolicy);

        if (run.Result.Metrics is null)
        {
            WriteCoverageRejected(
                LocalAiMode,
                promptVersion,
                run.Result);

            return 3;
        }

        WriteJson(
            new
            {
                mode =
                    LocalAiMode,

                approvedModelId =
                    ApprovedModelId,

                approvedModelSha256 =
                    ApprovedModelSha256,

                approvedRuntimeId =
                    ApprovedRuntimeId,

                runtimeProvenanceVerifiedByRunner =
                    false,

                promptVersion,

                evaluation =
                    CreatePromptSummary(
                        promptVersion,
                        run),

                persisted =
                    false,

                mayEnableRuntimeShadowMode =
                    false,

                mayInfluencePersistence =
                    false
            });

        return 0;
    }

    private static async Task<int> RunPromptComparisonAsync(
        BillStatementAiPrivateCorpusLoader loader,
        HttpClient httpClient,
        string fullCorpusRoot,
        IReadOnlyList<string> caseIds,
        string apiKey,
        BillStatementAiShadowReadinessPolicy readinessPolicy)
    {
        var baselineRun =
            await EvaluatePromptAsync(
                loader,
                httpClient,
                fullCorpusRoot,
                caseIds,
                apiKey,
                LocalAiBillStatementPromptCatalog.Version1,
                readinessPolicy);

        if (baselineRun.Result.Metrics is null)
        {
            WriteCoverageRejected(
                ComparePromptsMode,
                LocalAiBillStatementPromptCatalog.Version1,
                baselineRun.Result);

            return 3;
        }

        var candidateRun =
            await EvaluatePromptAsync(
                loader,
                httpClient,
                fullCorpusRoot,
                caseIds,
                apiKey,
                LocalAiBillStatementPromptCatalog.Version2,
                readinessPolicy);

        if (candidateRun.Result.Metrics is null)
        {
            WriteCoverageRejected(
                ComparePromptsMode,
                LocalAiBillStatementPromptCatalog.Version2,
                candidateRun.Result);

            return 3;
        }

        var baselineFieldScores =
            baselineRun.Result.FieldScores ??
            throw new InvalidOperationException(
                "A completed baseline prompt evaluation requires field scores.");

        var candidateFieldScores =
            candidateRun.Result.FieldScores ??
            throw new InvalidOperationException(
                "A completed candidate prompt evaluation requires field scores.");

        var baselineProviderScores =
            baselineRun.Result.ProviderScores ??
            throw new InvalidOperationException(
                "A completed baseline prompt evaluation requires anonymous provider scores.");

        var candidateProviderScores =
            candidateRun.Result.ProviderScores ??
            throw new InvalidOperationException(
                "A completed candidate prompt evaluation requires anonymous provider scores.");

        var baselineProviderFields =
            baselineRun.Result.ProviderFieldScores ??
            throw new InvalidOperationException(
                "A completed baseline prompt evaluation requires anonymous provider field scores.");

        var candidateProviderFields =
            candidateRun.Result.ProviderFieldScores ??
            throw new InvalidOperationException(
                "A completed candidate prompt evaluation requires anonymous provider field scores.");

        var comparison =
            new BillStatementAiPromptComparisonEvaluator()
                .CompareWithProviderFields(
                    baselineRun.Result.Metrics,
                    baselineFieldScores,
                    baselineProviderScores,
                    baselineProviderFields,
                    candidateRun.Result.Metrics,
                    candidateFieldScores,
                    candidateProviderScores,
                    candidateProviderFields);

        var baselineLatency =
            baselineRun.Result.ProviderAttemptLatency ??
            throw new InvalidOperationException(
                "A completed baseline prompt evaluation requires provider-attempt latency metrics.");

        var candidateLatency =
            candidateRun.Result.ProviderAttemptLatency ??
            throw new InvalidOperationException(
                "A completed candidate prompt evaluation requires provider-attempt latency metrics.");

        WriteJson(
            new
            {
                mode =
                    ComparePromptsMode,

                approvedModelId =
                    ApprovedModelId,

                approvedModelSha256 =
                    ApprovedModelSha256,

                approvedRuntimeId =
                    ApprovedRuntimeId,

                runtimeProvenanceVerifiedByRunner =
                    false,

                baseline =
                    CreatePromptSummary(
                        LocalAiBillStatementPromptCatalog.Version1,
                        baselineRun),

                candidate =
                    CreatePromptSummary(
                        LocalAiBillStatementPromptCatalog.Version2,
                        candidateRun),

                comparison =
                    new
                    {
                        comparison.FactPrecisionDelta,
                        comparison.FactRecallDelta,
                        comparison.ReadyCandidateRateDelta,
                        comparison.ProviderFailureRateDelta,
                        comparison.CorrectFactCountDelta,
                        comparison.IncorrectFactCountDelta,
                        comparison.MissedFactCountDelta,
                        comparison.ReadyCandidateStatementCountDelta,
                        comparison.ProviderFailureCountDelta,

                        fieldComparisons =
                            comparison.FieldComparisons.Select(
                                field =>
                                    new
                                    {
                                        field.FieldKey,
                                        field.BaselineCorrect,
                                        field.CandidateCorrect,
                                        field.CorrectDelta,
                                        field.BaselineIncorrect,
                                        field.CandidateIncorrect,
                                        field.IncorrectDelta,
                                        field.BaselineMissed,
                                        field.CandidateMissed,
                                        field.MissedDelta,
                                        field.BaselinePrecision,
                                        field.CandidatePrecision,
                                        field.PrecisionDelta,
                                        field.BaselineRecall,
                                        field.CandidateRecall,
                                        field.RecallDelta,
                                        field.CandidateHasNoRegression
                                    }),

                        providerComparisons =
                            comparison.ProviderComparisons.Select(
                                provider =>
                                    new
                                    {
                                        provider.ProviderOrdinal,
                                        provider.StatementCount,
                                        provider.BaselineCorrect,
                                        provider.CandidateCorrect,
                                        provider.CorrectDelta,
                                        provider.BaselineIncorrect,
                                        provider.CandidateIncorrect,
                                        provider.IncorrectDelta,
                                        provider.BaselineMissed,
                                        provider.CandidateMissed,
                                        provider.MissedDelta,
                                        provider.BaselinePrecision,
                                        provider.CandidatePrecision,
                                        provider.PrecisionDelta,
                                        provider.BaselineRecall,
                                        provider.CandidateRecall,
                                        provider.RecallDelta,
                                        provider.BaselineReadyCandidateRate,
                                        provider.CandidateReadyCandidateRate,
                                        provider.ReadyCandidateRateDelta,
                                        provider.BaselineProviderFailureRate,
                                        provider.CandidateProviderFailureRate,
                                        provider.ProviderFailureRateDelta,
                                        provider.CandidateHasNoRegression
                                    }),

                        comparison.CandidateHasNoAggregateRegression,
                        comparison.CandidateHasNoFieldRegression,
                        comparison.CandidateHasNoProviderRegression,
                        comparison.CandidateHasNoProviderFieldRegression,
                        comparison.RegressedProviderFieldCount,
                        comparison.CandidateHasStrictAggregateImprovement,
                        comparison.RegressedFieldKeys,
                        comparison.RegressedProviderOrdinals,
                        comparison.CandidateQualifiesForPromotionReview
                    },

                performanceComparison =
                    new
                    {
                        meanMillisecondsDelta =
                            candidateLatency.MeanMilliseconds -
                            baselineLatency.MeanMilliseconds,

                        p50MillisecondsDelta =
                            candidateLatency.P50Milliseconds -
                            baselineLatency.P50Milliseconds,

                        p95MillisecondsDelta =
                            candidateLatency.P95Milliseconds -
                            baselineLatency.P95Milliseconds,

                        maximumMillisecondsDelta =
                            candidateLatency.MaximumMilliseconds -
                            baselineLatency.MaximumMilliseconds,

                        latencyIsReportedNotPromotionGated =
                            true
                    },

                comparisonPolicy =
                    new
                    {
                        requiresSameEvaluationPopulation =
                            true,

                        requiresSameExpectedFactCountPerField =
                            true,

                        aggregatePrecisionMustNotDecrease =
                            true,

                        aggregateRecallMustNotDecrease =
                            true,

                        readyCandidateRateMustNotDecrease =
                            true,

                        providerFailureRateMustNotIncrease =
                            true,

                        fieldCorrectCountMustNotDecrease =
                            true,

                        fieldIncorrectCountMustNotIncrease =
                            true,

                        fieldMissedCountMustNotIncrease =
                            true,

                        fieldPrecisionMustNotDecrease =
                            true,

                        fieldRecallMustNotDecrease =
                            true,

                        requiresSameStatementPopulationPerAnonymousProvider =
                            true,

                        requiresSameExpectedFactCountPerAnonymousProvider =
                            true,

                        providerCorrectCountMustNotDecrease =
                            true,

                        providerIncorrectCountMustNotIncrease =
                            true,

                        providerMissedCountMustNotIncrease =
                            true,

                        providerPrecisionMustNotDecrease =
                            true,

                        providerRecallMustNotDecrease =
                            true,

                        providerReadyCandidateRateMustNotDecrease =
                            true,

                        providerFailureRatePerBucketMustNotIncrease =
                            true,

                        requiresSameExpectedFactCountPerAnonymousProviderField =
                            true,

                        providerFieldCorrectCountMustNotDecrease =
                            true,

                        providerFieldIncorrectCountMustNotIncrease =
                            true,

                        providerFieldMissedCountMustNotIncrease =
                            true,

                        requiresAtLeastOneStrictAggregateImprovement =
                            true
                    },

                persisted =
                    false,

                mayEnableRuntimeShadowMode =
                    false,

                mayInfluencePersistence =
                    false
            });

        return comparison
                .CandidateQualifiesForPromotionReview
            ? 0
            : 4;
    }

    private static async Task<PromptEvaluationRun> EvaluatePromptAsync(
        BillStatementAiPrivateCorpusLoader loader,
        HttpClient httpClient,
        string fullCorpusRoot,
        IReadOnlyList<string> caseIds,
        string apiKey,
        string promptVersion,
        BillStatementAiShadowReadinessPolicy readinessPolicy)
    {
        var options =
            new LocalAiBillStatementOptions
            {
                Enabled =
                    true,

                Model =
                    "fullworth-local",

                ApiKey =
                    apiKey,

                Endpoint =
                    "http://127.0.0.1:8080/v1/chat/completions",

                PromptVersion =
                    promptVersion
            };

        var validation =
            new LocalAiBillStatementOptionsValidator()
                .Validate(
                    null,
                    options);

        if (validation.Failed)
        {
            throw new InvalidOperationException(
                "Local model evaluation configuration is invalid.");
        }

        var stopwatch =
            Stopwatch.StartNew();

        var result =
            await new BillStatementAiPrivateCorpusProviderEvaluator(
                loader,
                new BillStatementAiPrivateCorpusCoverageGate(),
                new LocalAiBillStatementAiExtractor(
                    httpClient,
                    Options.Create(
                        options)),
                new BillStatementAiCandidateConversionService(
                    new BillStatementAiCandidateValidator()),
                new BillStatementAiGroundTruthScorer())
                .EvaluateAsync(
                    fullCorpusRoot,
                    caseIds,
                    promptVersion,
                    providerCallsAuthorized:
                        true,
                    readinessPolicy);

        stopwatch.Stop();

        var readiness =
            result.Metrics is null
                ? null
                : new BillStatementAiShadowReadinessEvaluator()
                    .Evaluate(
                        result.Metrics,
                        readinessPolicy);

        return new PromptEvaluationRun(
            Result:
                result,

            Readiness:
                readiness,

            ElapsedSeconds:
                stopwatch.Elapsed.TotalSeconds);
    }

    private static object CreatePromptSummary(
        string promptVersion,
        PromptEvaluationRun run)
    {
        var metrics =
            run.Result.Metrics ??
            throw new InvalidOperationException(
                "A prompt summary requires completed aggregate metrics.");

        var readiness =
            run.Readiness ??
            throw new InvalidOperationException(
                "A prompt summary requires aggregate readiness rates.");

        var fieldScores =
            run.Result.FieldScores ??
            throw new InvalidOperationException(
                "A completed prompt summary requires field scores.");

        var providerScores =
            run.Result.ProviderScores ??
            throw new InvalidOperationException(
                "A completed prompt summary requires anonymous provider scores.");

        var providerAttemptLatency =
            run.Result.ProviderAttemptLatency ??
            throw new InvalidOperationException(
                "A completed prompt summary requires aggregate provider-attempt latency metrics.");

        var failureKindCounts =
            run.Result.FailureKindCounts ??
            throw new InvalidOperationException(
                "A completed prompt summary requires aggregate provider failure-kind counts.");

        var inferenceCallCount =
            run.Result.InferenceCallCount ??
            throw new InvalidOperationException(
                "A completed prompt summary requires aggregate inference-call accounting.");

        return new
        {
            promptVersion,
            run.Result.ProviderEvaluationStarted,
            run.Result.Coverage.CaseCount,
            run.Result.Coverage.DistinctProviderCount,
            run.Result.Coverage.MinimumCasesForAnyProvider,
            metrics.ProviderAttemptCount,
            inferenceCallCount,
            metrics.ProviderFailureCount,
            metrics.ReadyCandidateStatementCount,
            metrics.CorrectFactCount,
            metrics.IncorrectFactCount,
            metrics.MissedFactCount,
            readiness.FactPrecision,
            readiness.FactRecall,
            readiness.ReadyCandidateRate,
            readiness.ProviderFailureRate,

            fieldScores =
                fieldScores.Select(
                    field =>
                        new
                        {
                            field.FieldKey,
                            field.Correct,
                            field.Incorrect,
                            field.Missed,
                            field.ExpectedFactCount,
                            field.PredictedFactCount,
                            field.Precision,
                            field.Recall
                        }),

            anonymousProviderScores =
                providerScores.Select(
                    provider =>
                        new
                        {
                            provider.ProviderOrdinal,
                            provider.StatementCount,
                            provider.ProviderAttemptCount,
                            provider.ProviderFailureCount,
                            provider.ReadyCandidateStatementCount,
                            provider.CorrectFactCount,
                            provider.IncorrectFactCount,
                            provider.MissedFactCount,
                            provider.FactPrecision,
                            provider.FactRecall,
                            provider.ReadyCandidateRate,
                            provider.ProviderFailureRate
                        }),

            providerAttemptLatency =
                new
                {
                    providerAttemptLatency.AttemptCount,
                    providerAttemptLatency.MinimumMilliseconds,
                    providerAttemptLatency.MeanMilliseconds,
                    providerAttemptLatency.P50Milliseconds,
                    providerAttemptLatency.P95Milliseconds,
                    providerAttemptLatency.MaximumMilliseconds
                },

            providerFailureKinds =
                failureKindCounts.Select(
                    failure =>
                        new
                        {
                            failureKind =
                                failure.FailureKind.ToString(),

                            failure.Count
                        }),

            meetsFullShadowAccuracyGate =
                readiness.MeetsShadowAccuracyGate,

            readiness.Failures,
            run.ElapsedSeconds
        };
    }

    private static void WriteCoverageRejected(
        string mode,
        string promptVersion,
        BillStatementAiPrivateCorpusProviderEvaluationResult result)
    {
        WriteJson(
            new
            {
                mode,

                approvedModelId =
                    ApprovedModelId,

                approvedModelSha256 =
                    ApprovedModelSha256,

                approvedRuntimeId =
                    ApprovedRuntimeId,

                runtimeProvenanceVerifiedByRunner =
                    false,

                promptVersion,
                result.ProviderEvaluationStarted,
                result.Coverage.CaseCount,
                result.Coverage.DistinctProviderCount,
                result.Coverage.MinimumCasesForAnyProvider,
                result.CoverageDecision.RequiredCaseCount,
                result.CoverageDecision.Failures,
                persisted =
                    false,

                mayEnableRuntimeShadowMode =
                    false,

                mayInfluencePersistence =
                    false
            });
    }

    private static bool TryParseArguments(
        string[] args,
        out EvaluationArguments parsed)
    {
        parsed =
            new EvaluationArguments(
                Mode:
                    string.Empty,

                CorpusRoot:
                    string.Empty,

                LocalInferenceAuthorized:
                    false,

                PromptVersion:
                    LocalAiBillStatementPromptCatalog.CurrentVersion);

        if (args.Length <
            1)
        {
            return false;
        }

        var mode =
            args[0];

        if (mode is not
            BaselineMode and not
            LocalAiMode and not
            ComparePromptsMode)
        {
            return false;
        }

        string? corpusRoot =
            null;

        string? promptVersion =
            null;

        var localInferenceAuthorized =
            false;

        for (var index = 1;
             index <
             args.Length;)
        {
            switch (args[index])
            {
                case "--corpus-root":
                    if (corpusRoot is not null ||
                        index + 1 >=
                        args.Length)
                    {
                        return false;
                    }

                    corpusRoot =
                        args[index + 1];

                    index +=
                        2;

                    break;

                case "--authorize-local-model-inference":
                    if (localInferenceAuthorized)
                    {
                        return false;
                    }

                    localInferenceAuthorized =
                        true;

                    index++;

                    break;

                case "--prompt-version":
                    if (mode !=
                            LocalAiMode ||
                        promptVersion is not null ||
                        index + 1 >=
                        args.Length)
                    {
                        return false;
                    }

                    promptVersion =
                        args[index + 1];

                    index +=
                        2;

                    break;

                default:
                    return false;
            }
        }

        if (string.IsNullOrWhiteSpace(
                corpusRoot) ||
            !Path.IsPathFullyQualified(
                corpusRoot))
        {
            return false;
        }

        if (mode ==
                BaselineMode &&
            (localInferenceAuthorized ||
             promptVersion is not null))
        {
            return false;
        }

        var selectedPromptVersion =
            promptVersion ??
            LocalAiBillStatementPromptCatalog
                .CurrentVersion;

        if (mode ==
                LocalAiMode &&
            !LocalAiBillStatementPromptCatalog
                .IsSupported(
                    selectedPromptVersion))
        {
            return false;
        }

        if (mode ==
                ComparePromptsMode &&
            promptVersion is not null)
        {
            return false;
        }

        parsed =
            new EvaluationArguments(
                Mode:
                    mode,

                CorpusRoot:
                    corpusRoot,

                LocalInferenceAuthorized:
                    localInferenceAuthorized,

                PromptVersion:
                    selectedPromptVersion);

        return true;
    }

    private static void WriteJson(
        object value)
    {
        Console.WriteLine(
            JsonSerializer.Serialize(
                value,
                JsonOptions));
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "Usage:");

        Console.Error.WriteLine(
            "  FullWorth.AiEvaluation baseline --corpus-root <absolute-private-corpus-path>");

        Console.Error.WriteLine(
            "  FullWorth.AiEvaluation local-ai --corpus-root <absolute-private-corpus-path> --authorize-local-model-inference [--prompt-version <version>]");

        Console.Error.WriteLine(
            "  FullWorth.AiEvaluation compare-prompts --corpus-root <absolute-private-corpus-path> --authorize-local-model-inference");

        Console.Error.WriteLine(
            $"Supported prompt versions: {string.Join(", ", LocalAiBillStatementPromptCatalog.SupportedVersions)}");

        Console.Error.WriteLine(
            "Local AI modes read FULLWORTH_LOCAL_AI_API_KEY from the process environment.");

        Console.Error.WriteLine(
            "compare-prompts returns exit code 4 when the candidate prompt does not qualify for promotion review.");
    }

    private sealed record EvaluationArguments(
        string Mode,
        string CorpusRoot,
        bool LocalInferenceAuthorized,
        string PromptVersion);

    private sealed record PromptEvaluationRun(
        BillStatementAiPrivateCorpusProviderEvaluationResult Result,
        BillStatementAiShadowReadinessResult? Readiness,
        double ElapsedSeconds);
}

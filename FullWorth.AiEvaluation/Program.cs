using System.Diagnostics;
using System.Text.Json;
using FullWorth.API.Services.Statements;
using Microsoft.Extensions.Options;

internal static class Program
{
    private const string ModelId =
        "qwen3-4b-q4-k-m";

    private const string ModelSha256 =
        "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5";

    private const string RuntimeId =
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
                out var mode,
                out var corpusRoot,
                out var localInferenceAuthorized))
        {
            PrintUsage();
            return 2;
        }

        if (mode == "local-ai" &&
            !localInferenceAuthorized)
        {
            Console.Error.WriteLine(
                "Local model evaluation requires --authorize-local-model-inference.");
            return 2;
        }

        try
        {
            var fullCorpusRoot =
                Path.GetFullPath(corpusRoot);

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

            if (mode == "baseline")
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
                        mode,
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
                        persisted = false
                    });

                return 0;
            }

            var apiKey =
                Environment.GetEnvironmentVariable(
                    "FULLWORTH_LOCAL_AI_API_KEY");

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine(
                    "FULLWORTH_LOCAL_AI_API_KEY must be provided through the process environment.");
                return 2;
            }

            var options =
                new LocalAiBillStatementOptions
                {
                    Enabled = true,
                    Model = "fullworth-local",
                    ApiKey = apiKey,
                    Endpoint = "http://127.0.0.1:8080/v1/chat/completions"
                };

            var validation =
                new LocalAiBillStatementOptionsValidator()
                    .Validate(
                        null,
                        options);

            if (validation.Failed)
            {
                Console.Error.WriteLine(
                    "Local model evaluation configuration is invalid.");
                return 2;
            }

            var readinessPolicy =
                BillStatementAiShadowReadinessPolicy.PrivateBetaDefault;

            using var httpClient =
                new HttpClient();

            var stopwatch =
                Stopwatch.StartNew();

            var result =
                await new BillStatementAiPrivateCorpusProviderEvaluator(
                    loader,
                    new BillStatementAiPrivateCorpusCoverageGate(),
                    new LocalAiBillStatementAiExtractor(
                        httpClient,
                        Options.Create(options)),
                    new BillStatementAiCandidateConversionService(
                        new BillStatementAiCandidateValidator()),
                    new BillStatementAiGroundTruthScorer())
                    .EvaluateAsync(
                        fullCorpusRoot,
                        caseIds,
                        options.PromptVersion,
                        providerCallsAuthorized:
                            localInferenceAuthorized,
                        readinessPolicy);

            stopwatch.Stop();

            if (result.Metrics is null)
            {
                WriteJson(
                    new
                    {
                        mode,
                        modelId = ModelId,
                        modelSha256 = ModelSha256,
                        runtimeId = RuntimeId,
                        result.ProviderEvaluationStarted,
                        result.Coverage.CaseCount,
                        result.Coverage.DistinctProviderCount,
                        result.Coverage.MinimumCasesForAnyProvider,
                        result.CoverageDecision.RequiredCaseCount,
                        result.CoverageDecision.Failures,
                        persisted = false
                    });

                return 3;
            }

            var readiness =
                new BillStatementAiShadowReadinessEvaluator()
                    .Evaluate(
                        result.Metrics,
                        readinessPolicy);

            var metrics =
                result.Metrics;

            WriteJson(
                new
                {
                    mode,
                    modelId = ModelId,
                    modelSha256 = ModelSha256,
                    runtimeId = RuntimeId,
                    promptVersion = options.PromptVersion,
                    result.ProviderEvaluationStarted,
                    result.Coverage.CaseCount,
                    result.Coverage.DistinctProviderCount,
                    result.Coverage.MinimumCasesForAnyProvider,
                    metrics.ProviderAttemptCount,
                    metrics.ProviderFailureCount,
                    metrics.ReadyCandidateStatementCount,
                    metrics.CorrectFactCount,
                    metrics.IncorrectFactCount,
                    metrics.MissedFactCount,
                    readiness.FactPrecision,
                    readiness.FactRecall,
                    readiness.ReadyCandidateRate,
                    readiness.ProviderFailureRate,
                    readiness.MeetsShadowAccuracyGate,
                    readiness.Failures,
                    elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                    persisted = false,
                    mayEnableRuntimeShadowMode = false,
                    mayInfluencePersistence = false
                });

            return 0;
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

    private static bool TryParseArguments(
        string[] args,
        out string mode,
        out string corpusRoot,
        out bool localInferenceAuthorized)
    {
        mode =
            args.Length > 0
                ? args[0]
                : string.Empty;

        corpusRoot =
            string.Empty;

        localInferenceAuthorized =
            args.Contains(
                "--authorize-local-model-inference",
                StringComparer.Ordinal);

        var expectedArgumentCount =
            mode == "baseline"
                ? 3
                : mode == "local-ai"
                    ? 4
                    : -1;

        if (args.Length !=
            expectedArgumentCount ||
            (mode == "baseline" &&
                localInferenceAuthorized) ||
            (mode == "local-ai" &&
                !localInferenceAuthorized))
        {
            return false;
        }

        var rootOptionIndex =
            Array.IndexOf(
                args,
                "--corpus-root");

        if (rootOptionIndex < 1 ||
            rootOptionIndex + 1 >= args.Length)
        {
            return false;
        }

        corpusRoot =
            args[rootOptionIndex + 1];

        return Path.IsPathFullyQualified(
            corpusRoot);
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
            "  FullWorth.AiEvaluation local-ai --corpus-root <absolute-private-corpus-path> --authorize-local-model-inference");
        Console.Error.WriteLine(
            "Local AI mode reads FULLWORTH_LOCAL_AI_API_KEY from the process environment.");
    }
}

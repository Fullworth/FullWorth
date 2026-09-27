using FullWorth.API.Services.Statements;
using System.Globalization;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiPrivateCorpusProviderEvaluatorTests
{
    [Fact]
    public async Task Evaluate_RequiresExplicitProviderAuthorizationBeforeCorpusRead()
    {
        var extractor =
            new FakeAiExtractor();

        var evaluator =
            CreateEvaluator(
                extractor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                evaluator.EvaluateAsync(
                    corpusRootDirectory:
                        Path.Combine(
                            Path.GetTempPath(),
                            $"missing-fullworth-corpus-{Guid.NewGuid():N}"),
                    caseIds:
                        [
                            "case-001",
                            "case-002"
                        ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        false,
                    readinessPolicy:
                        CreateReadinessPolicy()));

        Assert.Equal(
            0,
            extractor.CallCount);
    }

    [Fact]
    public async Task EvaluateLoadedCases_UsesTheValidatedInMemorySnapshot()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId: "provider-a-001",
            providerKey: "provider-a",
            totalAmount: 104.99m);
        directory.WriteCase(
            caseId: "provider-b-001",
            providerKey: "provider-b",
            totalAmount: 55m);

        var loader =
            new BillStatementAiPrivateCorpusLoader();
        var snapshot =
            await loader.LoadSnapshotAsync(
                directory.Path,
                [
                    "provider-a-001",
                    "provider-b-001"
                ]);

        // Simulate source changes between paired prompt runs. The second run
        // must keep using the already-validated statement and labels.
        directory.WriteCase(
            caseId: "provider-a-001",
            providerKey: "provider-a",
            totalAmount: 999m);
        directory.WriteCase(
            caseId: "provider-b-001",
            providerKey: "provider-b",
            totalAmount: 888m);

        var extractor =
            new FakeAiExtractor();

        var result =
            await CreateEvaluator(extractor)
                .EvaluateLoadedCasesAsync(
                    snapshot,
                    promptVersion: "offline-test-v1",
                    providerCallsAuthorized: true,
                    readinessPolicy: CreateReadinessPolicy());

        Assert.True(result.ProviderEvaluationStarted);
        Assert.Equal(2, extractor.CallCount);
        Assert.Equal(0, result.Metrics!.ProviderFailureCount);
        Assert.Equal(2, result.Metrics.EvaluatedStatementCount);
        Assert.Equal(8, result.Metrics.CorrectFactCount);
    }

    [Fact]
    public async Task Evaluate_RejectsInsufficientCoverageBeforeProviderCall()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-a-002",
            providerKey:
                "provider-a",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor();

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-a-002"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        Assert.False(
            result.ProviderEvaluationStarted);

        Assert.Null(
            result.Metrics);

        Assert.Null(
            result.FieldScores);

        Assert.Null(
            result.ProviderScores);

        Assert.Null(
            result.ProviderFieldScores);

        Assert.Null(
            result.InferenceCallCount);

        Assert.Null(
            result.MultiInferenceStatementCount);

        Assert.Null(
            result.MaximumInferenceCallsPerStatement);

        Assert.Null(
            result.ChunkedExtractionRejectedStatementCount);

        Assert.Null(
            result.ProviderAttemptLatency);

        Assert.Null(
            result.FailureKindCounts);

        Assert.False(
            result.CoverageDecision
                .MayBeginOfflineProviderEvaluation);

        Assert.NotEmpty(
            result.CoverageDecision.Failures);

        Assert.Equal(
            0,
            extractor.CallCount);

        Assert.False(
            result.MayEnableRuntimeShadowMode);

        Assert.False(
            result.MayInfluencePersistence);
    }

    [Fact]
    public async Task Evaluate_ProducesAggregateMetricsFromValidatedCandidates()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor(
                inferenceCallsPerAttempt:
                    2);

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        Assert.True(
            result.ProviderEvaluationStarted);

        Assert.True(
            result.CoverageDecision
                .MayBeginOfflineProviderEvaluation);

        Assert.Equal(
            2,
            result.Coverage.CaseCount);

        Assert.Equal(
            2,
            result.Coverage.DistinctProviderCount);

        Assert.Equal(
            1,
            result.Coverage.MinimumCasesForAnyProvider);

        var metrics =
            Assert.IsType<BillStatementAiShadowReadinessMetrics>(
                result.Metrics);

        Assert.Equal(
            2,
            metrics.EvaluatedStatementCount);

        Assert.Equal(
            2,
            metrics.DistinctProviderCount);

        Assert.Equal(
            1,
            metrics.MinimumStatementsForAnyProvider);

        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            4L,
            result.InferenceCallCount);

        Assert.Equal(
            2L,
            result.MultiInferenceStatementCount);

        Assert.Equal(
            2L,
            result.MaximumInferenceCallsPerStatement);

        Assert.Equal(
            0L,
            result.ChunkedExtractionRejectedStatementCount);

        Assert.Equal(
            0,
            metrics.ProviderFailureCount);

        Assert.Equal(
            2,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            8,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            0,
            metrics.MissedFactCount);

        /*
         * This evaluator deliberately does not invent false-alert
         * measurements. That requires a separate deterministic evaluation.
         */
        Assert.Equal(
            0,
            metrics.AlertEvaluatedStatementCount);

        Assert.Equal(
            0,
            metrics.FalseAlertStatementCount);

        var fieldScores =
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiFieldScore>>(
                result.FieldScores);

        Assert.Equal(
            BillStatementAiGroundTruthFieldKeys.All,
            fieldScores.Select(
                field =>
                    field.FieldKey));

        Assert.Equal(
            metrics.CorrectFactCount,
            fieldScores.Sum(
                field =>
                    field.Correct));

        Assert.Equal(
            metrics.IncorrectFactCount,
            fieldScores.Sum(
                field =>
                    field.Incorrect));

        Assert.Equal(
            metrics.MissedFactCount,
            fieldScores.Sum(
                field =>
                    field.Missed));

        var providerScores =
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiProviderScore>>(
                result.ProviderScores);

        Assert.Equal(
            2,
            providerScores.Count);

        var providerFieldScores =
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiProviderFieldScore>>(
                result.ProviderFieldScores);

        Assert.Equal(
            providerScores.Count,
            providerFieldScores.Count);

        for (var index = 0;
             index < providerScores.Count;
             index++)
        {
            Assert.Equal(
                providerScores[index].ProviderOrdinal,
                providerFieldScores[index].ProviderOrdinal);

            Assert.Equal(
                BillStatementAiGroundTruthFieldKeys.All,
                providerFieldScores[index].FieldScores.Select(
                    field =>
                        field.FieldKey));

            Assert.Equal(
                providerScores[index].CorrectFactCount,
                providerFieldScores[index].FieldScores.Sum(
                    field =>
                        field.Correct));

            Assert.Equal(
                providerScores[index].MissedFactCount,
                providerFieldScores[index].FieldScores.Sum(
                    field =>
                        field.Missed));
        }

        var serializedProviderFields =
            System.Text.Json.JsonSerializer.Serialize(
                providerFieldScores);

        Assert.DoesNotContain(
            "provider-a",
            serializedProviderFields,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "provider-b",
            serializedProviderFields,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            new[]
            {
                1,
                2
            },
            providerScores.Select(
                score =>
                    score.ProviderOrdinal));

        Assert.All(
            providerScores,
            score =>
            {
                Assert.Equal(
                    1,
                    score.StatementCount);

                Assert.Equal(
                    1,
                    score.ProviderAttemptCount);

                Assert.Equal(
                    0,
                    score.ProviderFailureCount);

                Assert.Equal(
                    1,
                    score.ReadyCandidateStatementCount);

                Assert.Equal(
                    4,
                    score.CorrectFactCount);

                Assert.Equal(
                    0,
                    score.IncorrectFactCount);

                Assert.Equal(
                    0,
                    score.MissedFactCount);

                Assert.Equal(
                    1m,
                    score.FactPrecision);

                Assert.Equal(
                    1m,
                    score.FactRecall);

                Assert.Equal(
                    1m,
                    score.ReadyCandidateRate);

                Assert.Equal(
                    0m,
                    score.ProviderFailureRate);
            });

        Assert.Equal(
            metrics.EvaluatedStatementCount,
            providerScores.Sum(
                score =>
                    score.StatementCount));

        Assert.Equal(
            metrics.ProviderAttemptCount,
            providerScores.Sum(
                score =>
                    score.ProviderAttemptCount));

        Assert.Equal(
            metrics.ProviderFailureCount,
            providerScores.Sum(
                score =>
                    score.ProviderFailureCount));

        Assert.Equal(
            metrics.ReadyCandidateStatementCount,
            providerScores.Sum(
                score =>
                    score.ReadyCandidateStatementCount));

        Assert.Equal(
            metrics.CorrectFactCount,
            providerScores.Sum(
                score =>
                    score.CorrectFactCount));

        Assert.Equal(
            metrics.IncorrectFactCount,
            providerScores.Sum(
                score =>
                    score.IncorrectFactCount));

        Assert.Equal(
            metrics.MissedFactCount,
            providerScores.Sum(
                score =>
                    score.MissedFactCount));

        Assert.DoesNotContain(
            typeof(
                    BillStatementAiProviderScore)
                .GetProperties(),
            property =>
                property.Name.Contains(
                    "Key",
                    StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains(
                    "Name",
                    StringComparison.OrdinalIgnoreCase));

        Assert.DoesNotContain(
            "provider-a",
            string.Join(
                "|",
                providerScores),
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "provider-b",
            string.Join(
                "|",
                providerScores),
            StringComparison.OrdinalIgnoreCase);

        var latency =
            Assert.IsType<
                BillStatementAiProviderAttemptLatencySummary>(
                result.ProviderAttemptLatency);

        Assert.Equal(
            metrics.ProviderAttemptCount,
            latency.AttemptCount);

        Assert.True(
            latency.MinimumMilliseconds >=
            0d);

        Assert.InRange(
            latency.MeanMilliseconds,
            latency.MinimumMilliseconds,
            latency.MaximumMilliseconds);

        Assert.InRange(
            latency.P50Milliseconds,
            latency.MinimumMilliseconds,
            latency.MaximumMilliseconds);

        Assert.InRange(
            latency.P95Milliseconds,
            latency.P50Milliseconds,
            latency.MaximumMilliseconds);

        Assert.DoesNotContain(
            typeof(
                    BillStatementAiProviderAttemptLatencySummary)
                .GetProperties(),
            property =>
                property.PropertyType !=
                    typeof(long) &&
                property.PropertyType !=
                    typeof(double));

        var failureKindCounts =
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiExtractionFailureCount>>(
                result.FailureKindCounts);

        Assert.Empty(
            failureKindCounts);

        var totalAmount =
            Assert.Single(
                fieldScores,
                field =>
                    field.FieldKey ==
                    BillStatementAiGroundTruthFieldKeys.TotalAmount);

        Assert.Equal(
            2,
            totalAmount.Correct);

        Assert.Equal(
            1m,
            totalAmount.Precision);

        Assert.Equal(
            1m,
            totalAmount.Recall);

        var statementDate =
            Assert.Single(
                fieldScores,
                field =>
                    field.FieldKey ==
                    BillStatementAiGroundTruthFieldKeys.StatementDate);

        Assert.Equal(
            0,
            statementDate.ExpectedFactCount);

        Assert.Equal(
            0,
            statementDate.PredictedFactCount);

        Assert.Equal(
            2,
            extractor.CallCount);

        Assert.Equal(
            "offline-test-v1",
            extractor.LastPromptVersion);

        Assert.False(
            result.MayEnableRuntimeShadowMode);

        Assert.False(
            result.MayInfluencePersistence);

        /*
         * The result contract must remain aggregate-only.
         */
        var propertyNames =
            typeof(
                    BillStatementAiPrivateCorpusProviderEvaluationResult)
                .GetProperties()
                .Select(
                    property =>
                        property.Name)
                .ToArray();

        Assert.DoesNotContain(
            propertyNames,
            name =>
                name.Contains(
                    "Text",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Path",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "CaseId",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Evidence",
                    StringComparison.OrdinalIgnoreCase) ||
                name.Contains(
                    "Candidate",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Evaluate_OversizedStatements_UseChunkedInferenceAndReconcile()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new ChunkAwareAiExtractor();

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy(),
                    maxCharactersPerInference:
                        40);

        var metrics =
            Assert.IsType<
                BillStatementAiShadowReadinessMetrics>(
                    result.Metrics);

        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            0,
            metrics.ProviderFailureCount);

        Assert.Equal(
            2,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            8,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            0,
            metrics.MissedFactCount);

        Assert.Equal(
            8,
            extractor.CallCount);

        Assert.Equal(
            8L,
            result.InferenceCallCount);

        Assert.Equal(
            2L,
            result.MultiInferenceStatementCount);

        Assert.Equal(
            4L,
            result.MaximumInferenceCallsPerStatement);

        Assert.Equal(
            0L,
            result.ChunkedExtractionRejectedStatementCount);

        Assert.All(
            extractor.Requests,
            request =>
                Assert.InRange(
                    request.DocumentText.Length,
                    1,
                    40));
    }

    [Fact]
    public async Task Evaluate_ChunkedCandidateRejection_IsCountedSeparately()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var result =
            await CreateEvaluator(
                    new RejectingChunkAiExtractor())
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy(),
                    maxCharactersPerInference:
                        40);

        var metrics =
            Assert.IsType<
                BillStatementAiShadowReadinessMetrics>(
                    result.Metrics);

        Assert.Equal(
            2L,
            result.ChunkedExtractionRejectedStatementCount);

        Assert.Equal(
            0,
            metrics.ProviderFailureCount);

        Assert.Equal(
            0,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            8,
            metrics.MissedFactCount);

        Assert.Empty(
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiExtractionFailureCount>>(
                    result.FailureKindCounts));
    }

    [Fact]
    public async Task Evaluate_ProviderFailureIsCountedWithoutExposingFailureDetails()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor(
                failWhenDocumentContains:
                    "$55.00");

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        Assert.True(
            result.ProviderEvaluationStarted);

        var metrics =
            Assert.IsType<BillStatementAiShadowReadinessMetrics>(
                result.Metrics);

        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            1,
            metrics.ProviderFailureCount);

        Assert.Equal(
            1,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            4,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            4,
            metrics.MissedFactCount);

        var providerScores =
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiProviderScore>>(
                result.ProviderScores);

        Assert.Equal(
            2,
            providerScores.Count);

        Assert.Single(
            providerScores,
            score =>
                score.ProviderFailureCount ==
                    1 &&
                score.ProviderFailureRate ==
                    1m &&
                score.ReadyCandidateStatementCount ==
                    0);

        Assert.Single(
            providerScores,
            score =>
                score.ProviderFailureCount ==
                    0 &&
                score.ProviderFailureRate ==
                    0m &&
                score.ReadyCandidateStatementCount ==
                    1);

        Assert.Equal(
            2,
            Assert.IsType<
                    BillStatementAiProviderAttemptLatencySummary>(
                    result.ProviderAttemptLatency)
                .AttemptCount);

        var failureKind =
            Assert.Single(
                Assert.IsAssignableFrom<
                    IReadOnlyList<BillStatementAiExtractionFailureCount>>(
                    result.FailureKindCounts));

        Assert.Equal(
            BillStatementAiExtractionFailureKind.Unknown,
            failureKind.FailureKind);

        Assert.Equal(
            1,
            failureKind.Count);

        Assert.Equal(
            2,
            extractor.CallCount);

        Assert.DoesNotContain(
            "sensitive-provider-detail",
            result.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Evaluate_RejectedCandidateDoesNotBecomeTrustedExtraction()
    {
        using var directory =
            new TemporaryCorpusDirectory();

        directory.WriteCase(
            caseId:
                "provider-a-001",
            providerKey:
                "provider-a",
            totalAmount:
                104.99m);

        directory.WriteCase(
            caseId:
                "provider-b-001",
            providerKey:
                "provider-b",
            totalAmount:
                55m);

        var extractor =
            new FakeAiExtractor(
                returnUnsupportedEvidence:
                    true);

        var result =
            await CreateEvaluator(
                    extractor)
                .EvaluateAsync(
                    directory.Path,
                    [
                        "provider-a-001",
                        "provider-b-001"
                    ],
                    promptVersion:
                        "offline-test-v1",
                    providerCallsAuthorized:
                        true,
                    readinessPolicy:
                        CreateReadinessPolicy());

        var metrics =
            Assert.IsType<BillStatementAiShadowReadinessMetrics>(
                result.Metrics);

        /*
         * The provider successfully returned two candidates, so these are
         * attempts rather than transport failures.
         */
        Assert.Equal(
            2,
            metrics.ProviderAttemptCount);

        Assert.Equal(
            0,
            metrics.ProviderFailureCount);

        /*
         * Evidence validation rejects both candidates. They therefore count
         * as missed truth, never as trusted extractions.
         */
        Assert.Equal(
            0,
            metrics.ReadyCandidateStatementCount);

        Assert.Equal(
            0,
            metrics.CorrectFactCount);

        Assert.Equal(
            0,
            metrics.IncorrectFactCount);

        Assert.Equal(
            8,
            metrics.MissedFactCount);

        var providerScores =
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiProviderScore>>(
                result.ProviderScores);

        Assert.All(
            providerScores,
            score =>
            {
                Assert.Equal(
                    0,
                    score.ReadyCandidateStatementCount);

                Assert.Equal(
                    0,
                    score.CorrectFactCount);

                Assert.Equal(
                    4,
                    score.MissedFactCount);

                Assert.Equal(
                    0m,
                    score.FactRecall);
            });

        Assert.Equal(
            2,
            Assert.IsType<
                    BillStatementAiProviderAttemptLatencySummary>(
                    result.ProviderAttemptLatency)
                .AttemptCount);

        Assert.Empty(
            Assert.IsAssignableFrom<
                IReadOnlyList<BillStatementAiExtractionFailureCount>>(
                result.FailureKindCounts));
    }

    [Fact]
    public void ProviderAttemptLatencySummary_UsesAggregateNearestRankPercentiles()
    {
        var summary =
            BillStatementAiProviderAttemptLatencySummary
                .Create(
                    new[]
                    {
                        10d,
                        20d,
                        30d,
                        40d,
                        100d
                    });

        Assert.Equal(
            5,
            summary.AttemptCount);

        Assert.Equal(
            10d,
            summary.MinimumMilliseconds);

        Assert.Equal(
            40d,
            summary.MeanMilliseconds);

        Assert.Equal(
            30d,
            summary.P50Milliseconds);

        Assert.Equal(
            100d,
            summary.P95Milliseconds);

        Assert.Equal(
            100d,
            summary.MaximumMilliseconds);
    }

    private static BillStatementAiPrivateCorpusProviderEvaluator
        CreateEvaluator(
            IBillStatementAiExtractor extractor)
    {
        return new BillStatementAiPrivateCorpusProviderEvaluator(
            new BillStatementAiPrivateCorpusLoader(),
            new BillStatementAiPrivateCorpusCoverageGate(),
            extractor,
            new BillStatementAiCandidateConversionService(
                new BillStatementAiCandidateValidator()),
            new BillStatementAiGroundTruthScorer());
    }

    private static BillStatementAiShadowReadinessPolicy
        CreateReadinessPolicy()
    {
        return new BillStatementAiShadowReadinessPolicy(
            MinimumEvaluatedStatementCount:
                2,
            MinimumDistinctProviderCount:
                2,
            MinimumStatementsPerProvider:
                1,
            MinimumProviderAttemptCount:
                2,
            MinimumAlertEvaluatedStatementCount:
                2,
            MinimumFactPrecision:
                0.99m,
            MinimumFactRecall:
                0.95m,
            MinimumReadyCandidateRate:
                0.85m,
            MaximumFalseAlertRate:
                0.01m,
            MaximumProviderFailureRate:
                0.50m);
    }

    private sealed class RejectingChunkAiExtractor
        : IBillStatementAiExtractor,
          IBillStatementAiInferenceCallCounter
    {
        public long InferenceCallCount { get; private set; }

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                request);

            cancellationToken.ThrowIfCancellationRequested();

            InferenceCallCount =
                checked(
                    InferenceCallCount +
                    1);

            return Task.FromResult(
                new BillStatementAiCandidate(
                    ProviderName:
                        null,
                    AccountIdentifierSuffix:
                        null,
                    BillingPeriodStart:
                        null,
                    BillingPeriodEnd:
                        null,
                    StatementDate:
                        null,
                    DueDate:
                        null,
                    PreviousBalance:
                        null,
                    Payments:
                        null,
                    CurrentCharges:
                        null,
                    TotalDue:
                        null,
                    CurrencyCode:
                        null,
                    PlanOrService:
                        null,
                    UsageSummary:
                        null,
                    LineItems:
                        [],
                    Evidence:
                        [
                            new BillStatementAiEvidence(
                                "unsupportedFact",
                                request.DocumentText[..1])
                        ],
                    ModelConfidence:
                        BillStatementAiModelConfidence.High));
        }
    }

    private sealed class ChunkAwareAiExtractor
        : IBillStatementAiExtractor,
          IBillStatementAiInferenceCallCounter
    {
        public int CallCount { get; private set; }

        public long InferenceCallCount { get; private set; }

        public List<BillStatementAiExtractionRequest> Requests
        {
            get;
        } =
            [];

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                request);

            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            InferenceCallCount =
                checked(
                    InferenceCallCount +
                    1);

            Requests.Add(
                request);

            decimal? totalDue =
                request.DocumentText.Contains(
                    "$104.99",
                    StringComparison.Ordinal)
                    ? 104.99m
                    : request.DocumentText.Contains(
                        "$55.00",
                        StringComparison.Ordinal)
                        ? 55m
                        : null;

            DateOnly? billingPeriodStart =
                request.DocumentText.Contains(
                    "Billing Period Start: 08/01/2026",
                    StringComparison.Ordinal)
                    ? new DateOnly(
                        2026,
                        8,
                        1)
                    : null;

            DateOnly? billingPeriodEnd =
                request.DocumentText.Contains(
                    "Billing Period End: 08/31/2026",
                    StringComparison.Ordinal)
                    ? new DateOnly(
                        2026,
                        8,
                        31)
                    : null;

            string? currencyCode =
                request.DocumentText.Contains(
                    "Currency: USD",
                    StringComparison.Ordinal)
                    ? "USD"
                    : null;

            var evidence =
                new List<BillStatementAiEvidence>();

            if (totalDue.HasValue)
            {
                evidence.Add(
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.TotalDue,
                        totalDue.Value ==
                            104.99m
                            ? "Total Due: $104.99"
                            : "Total Due: $55.00"));
            }

            if (billingPeriodStart.HasValue)
            {
                evidence.Add(
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.BillingPeriodStart,
                        "Billing Period Start: 08/01/2026"));
            }

            if (billingPeriodEnd.HasValue)
            {
                evidence.Add(
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.BillingPeriodEnd,
                        "Billing Period End: 08/31/2026"));
            }

            if (currencyCode is not null)
            {
                evidence.Add(
                    new BillStatementAiEvidence(
                        BillStatementAiFactKeys.CurrencyCode,
                        "Currency: USD"));
            }

            return Task.FromResult(
                new BillStatementAiCandidate(
                    ProviderName:
                        null,
                    AccountIdentifierSuffix:
                        null,
                    BillingPeriodStart:
                        billingPeriodStart,
                    BillingPeriodEnd:
                        billingPeriodEnd,
                    StatementDate:
                        null,
                    DueDate:
                        null,
                    PreviousBalance:
                        null,
                    Payments:
                        null,
                    CurrentCharges:
                        null,
                    TotalDue:
                        totalDue,
                    CurrencyCode:
                        currencyCode,
                    PlanOrService:
                        null,
                    UsageSummary:
                        null,
                    LineItems:
                        [],
                    Evidence:
                        evidence,
                    ModelConfidence:
                        BillStatementAiModelConfidence.High));
        }
    }

    private sealed class FakeAiExtractor
        : IBillStatementAiExtractor,
          IBillStatementAiInferenceCallCounter
    {
        private readonly string?
            _failWhenDocumentContains;

        private readonly bool
            _returnUnsupportedEvidence;

        private readonly int
            _inferenceCallsPerAttempt;

        public FakeAiExtractor(
            string? failWhenDocumentContains = null,
            bool returnUnsupportedEvidence = false,
            int inferenceCallsPerAttempt = 1)
        {
            if (inferenceCallsPerAttempt <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inferenceCallsPerAttempt));
            }

            _failWhenDocumentContains =
                failWhenDocumentContains;

            _returnUnsupportedEvidence =
                returnUnsupportedEvidence;

            _inferenceCallsPerAttempt =
                inferenceCallsPerAttempt;
        }

        public int CallCount { get; private set; }

        public long InferenceCallCount { get; private set; }

        public string? LastPromptVersion { get; private set; }

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(
                request);

            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;

            InferenceCallCount =
                checked(
                    InferenceCallCount +
                    _inferenceCallsPerAttempt);

            LastPromptVersion =
                request.PromptVersion;

            if (!string.IsNullOrWhiteSpace(
                    _failWhenDocumentContains) &&
                request.DocumentText.Contains(
                    _failWhenDocumentContains,
                    StringComparison.Ordinal))
            {
                throw new BillStatementAiExtractionException(
                    "sensitive-provider-detail");
            }

            var amount =
                request.DocumentText.Contains(
                    "$104.99",
                    StringComparison.Ordinal)
                    ? 104.99m
                    : request.DocumentText.Contains(
                        "$55.00",
                        StringComparison.Ordinal)
                        ? 55m
                        : throw new InvalidOperationException(
                            "Unexpected test statement.");

            IReadOnlyList<BillStatementAiEvidence> evidence =
                _returnUnsupportedEvidence
                    ?
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "This text does not exist in the statement."),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "This text does not exist in the statement."),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "This text does not exist in the statement."),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "This text does not exist in the statement.")
                    ]
                    :
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            amount ==
                                104.99m
                                ? "Total Due: $104.99"
                                : "Total Due: $55.00"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "Billing Period Start: 08/01/2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "Billing Period End: 08/31/2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "Currency: USD")
                    ];

            return Task.FromResult(
                new BillStatementAiCandidate(
                    ProviderName:
                        null,
                    AccountIdentifierSuffix:
                        null,
                    BillingPeriodStart:
                        new DateOnly(
                            2026,
                            8,
                            1),
                    BillingPeriodEnd:
                        new DateOnly(
                            2026,
                            8,
                            31),
                    StatementDate:
                        null,
                    DueDate:
                        null,
                    PreviousBalance:
                        null,
                    Payments:
                        null,
                    CurrentCharges:
                        null,
                    TotalDue:
                        amount,
                    CurrencyCode:
                        "USD",
                    PlanOrService:
                        null,
                    UsageSummary:
                        null,
                    LineItems:
                        [],
                    Evidence:
                        evidence,
                    ModelConfidence:
                        BillStatementAiModelConfidence.High));
        }
    }

    private sealed class TemporaryCorpusDirectory
        : IDisposable
    {
        public TemporaryCorpusDirectory()
        {
            Path =
                System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"fullworth-provider-corpus-test-{Guid.NewGuid():N}");

            Directory.CreateDirectory(
                Path);
        }

        public string Path { get; }

        public void WriteCase(
            string caseId,
            string providerKey,
            decimal totalAmount)
        {
            var caseDirectory =
                System.IO.Path.Combine(
                    Path,
                    caseId);

            Directory.CreateDirectory(
                caseDirectory);

            var amountText =
                totalAmount.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture);

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .StatementTextFileName),
                $$"""
                Total Due: ${{amountText}}
                Billing Period Start: 08/01/2026
                Billing Period End: 08/31/2026
                Currency: USD
                """);

            File.WriteAllText(
                System.IO.Path.Combine(
                    caseDirectory,
                    BillStatementAiPrivateCorpusPathPolicy
                        .GroundTruthFileName),
                $$"""
                {
                  "providerKey": "{{providerKey}}",
                  "totalAmount": {{amountText}},
                  "billingPeriodStart": "2026-08-01",
                  "billingPeriodEnd": "2026-08-31",
                  "statementDate": null,
                  "dueDate": null,
                  "currencyCode": "USD",
                  "lineItems": []
                }
                """);
        }

        public void Dispose()
        {
            if (Directory.Exists(
                    Path))
            {
                Directory.Delete(
                    Path,
                    recursive:
                        true);
            }
        }
    }
}
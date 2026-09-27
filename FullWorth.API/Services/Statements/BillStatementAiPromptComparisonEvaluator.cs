namespace FullWorth.API.Services.Statements;

/*
 * Pure aggregate comparison for two prompt evaluations run against the same
 * held-out statement population.
 *
 * This class never receives statement text, provider identity, evidence,
 * account information, case identifiers, or model output. It cannot enable
 * runtime shadow mode or AI-derived persistence.
 */
public sealed class BillStatementAiPromptComparisonEvaluator
{
    public BillStatementAiPromptComparisonResult Compare(
        BillStatementAiShadowReadinessMetrics baseline,
        IReadOnlyList<BillStatementAiFieldScore> baselineFields,
        IReadOnlyList<BillStatementAiProviderScore> baselineProviders,
        BillStatementAiShadowReadinessMetrics candidate,
        IReadOnlyList<BillStatementAiFieldScore> candidateFields,
        IReadOnlyList<BillStatementAiProviderScore> candidateProviders)
    {
        ArgumentNullException.ThrowIfNull(
            baseline);

        ArgumentNullException.ThrowIfNull(
            baselineFields);

        ArgumentNullException.ThrowIfNull(
            baselineProviders);

        ArgumentNullException.ThrowIfNull(
            candidate);

        ArgumentNullException.ThrowIfNull(
            candidateFields);

        ArgumentNullException.ThrowIfNull(
            candidateProviders);

        ValidatePopulation(
            baseline,
            candidate);

        ValidateFieldScores(
            baseline,
            baselineFields,
            nameof(baselineFields));

        ValidateFieldScores(
            candidate,
            candidateFields,
            nameof(candidateFields));

        ValidateProviderScores(
            baseline,
            baselineProviders,
            nameof(baselineProviders));

        ValidateProviderScores(
            candidate,
            candidateProviders,
            nameof(candidateProviders));

        var fieldComparisons =
            CompareFields(
                baselineFields,
                candidateFields);

        var providerComparisons =
            CompareProviders(
                baselineProviders,
                candidateProviders);

        var baselineFactPrecision =
            Divide(
                baseline.CorrectFactCount,
                baseline.CorrectFactCount +
                baseline.IncorrectFactCount);

        var candidateFactPrecision =
            Divide(
                candidate.CorrectFactCount,
                candidate.CorrectFactCount +
                candidate.IncorrectFactCount);

        var baselineFactRecall =
            Divide(
                baseline.CorrectFactCount,
                baseline.CorrectFactCount +
                baseline.MissedFactCount);

        var candidateFactRecall =
            Divide(
                candidate.CorrectFactCount,
                candidate.CorrectFactCount +
                candidate.MissedFactCount);

        var baselineReadyCandidateRate =
            Divide(
                baseline.ReadyCandidateStatementCount,
                baseline.ProviderAttemptCount);

        var candidateReadyCandidateRate =
            Divide(
                candidate.ReadyCandidateStatementCount,
                candidate.ProviderAttemptCount);

        var baselineProviderFailureRate =
            Divide(
                baseline.ProviderFailureCount,
                baseline.ProviderAttemptCount);

        var baselineScoredDocumentExactMatchRate =
            Divide(
                baseline.ScoredDocumentExactMatchCount,
                baseline.EvaluatedStatementCount);

        var candidateProviderFailureRate =
            Divide(
                candidate.ProviderFailureCount,
                candidate.ProviderAttemptCount);

        var candidateScoredDocumentExactMatchRate =
            Divide(
                candidate.ScoredDocumentExactMatchCount,
                candidate.EvaluatedStatementCount);

        var noAggregateRegression =
            candidateScoredDocumentExactMatchRate >=
                baselineScoredDocumentExactMatchRate &&
            candidateFactPrecision >=
                baselineFactPrecision &&
            candidateFactRecall >=
                baselineFactRecall &&
            candidateReadyCandidateRate >=
                baselineReadyCandidateRate &&
            candidateProviderFailureRate <=
                baselineProviderFailureRate;

        var hasStrictAggregateImprovement =
            candidateFactPrecision >
                baselineFactPrecision ||
            candidateFactRecall >
                baselineFactRecall ||
            candidateReadyCandidateRate >
                baselineReadyCandidateRate ||
            candidateProviderFailureRate <
                baselineProviderFailureRate ||
            candidate.ScoredDocumentExactMatchCount >
                baseline.ScoredDocumentExactMatchCount;

        var noFieldRegression =
            fieldComparisons.All(
                field =>
                    field.CandidateHasNoRegression);

        var noProviderRegression =
            providerComparisons.All(
                provider =>
                    provider.CandidateHasNoRegression);

        return new BillStatementAiPromptComparisonResult(
            BaselineFactPrecision:
                baselineFactPrecision,

            CandidateFactPrecision:
                candidateFactPrecision,

            FactPrecisionDelta:
                candidateFactPrecision -
                baselineFactPrecision,

            BaselineFactRecall:
                baselineFactRecall,

            CandidateFactRecall:
                candidateFactRecall,

            FactRecallDelta:
                candidateFactRecall -
                baselineFactRecall,

            BaselineReadyCandidateRate:
                baselineReadyCandidateRate,

            CandidateReadyCandidateRate:
                candidateReadyCandidateRate,

            ReadyCandidateRateDelta:
                candidateReadyCandidateRate -
                baselineReadyCandidateRate,

            BaselineProviderFailureRate:
                baselineProviderFailureRate,

            CandidateProviderFailureRate:
                candidateProviderFailureRate,

            ProviderFailureRateDelta:
                candidateProviderFailureRate -
                baselineProviderFailureRate,

            CorrectFactCountDelta:
                candidate.CorrectFactCount -
                baseline.CorrectFactCount,

            IncorrectFactCountDelta:
                candidate.IncorrectFactCount -
                baseline.IncorrectFactCount,

            MissedFactCountDelta:
                candidate.MissedFactCount -
                baseline.MissedFactCount,

            ReadyCandidateStatementCountDelta:
                candidate.ReadyCandidateStatementCount -
                baseline.ReadyCandidateStatementCount,

            ProviderFailureCountDelta:
                candidate.ProviderFailureCount -
                baseline.ProviderFailureCount,

            FieldComparisons:
                fieldComparisons,

            ProviderComparisons:
                providerComparisons,

            CandidateHasNoAggregateRegression:
                noAggregateRegression,

            CandidateHasNoFieldRegression:
                noFieldRegression,

            CandidateHasNoProviderRegression:
                noProviderRegression,

            CandidateHasStrictAggregateImprovement:
                hasStrictAggregateImprovement,

            CandidateQualifiesForPromotionReview:
                false)
        {
            BaselineScoredDocumentExactMatchRate =
                baselineScoredDocumentExactMatchRate,

            CandidateScoredDocumentExactMatchRate =
                candidateScoredDocumentExactMatchRate,

            ScoredDocumentExactMatchRateDelta =
                candidateScoredDocumentExactMatchRate -
                baselineScoredDocumentExactMatchRate,

            ScoredDocumentExactMatchCountDelta =
                candidate.ScoredDocumentExactMatchCount -
                baseline.ScoredDocumentExactMatchCount,

            CandidateHasNoScoredDocumentRegression =
                candidate.ScoredDocumentExactMatchCount >=
                baseline.ScoredDocumentExactMatchCount
        };
    }

    /*
     * The aggregate-only comparison remains available for diagnostics but
     * cannot qualify a prompt. Promotion review requires the intersection of
     * each anonymous provider bucket with each fixed field.
     */
    public BillStatementAiPromptComparisonResult CompareWithProviderFields(
        BillStatementAiShadowReadinessMetrics baseline,
        IReadOnlyList<BillStatementAiFieldScore> baselineFields,
        IReadOnlyList<BillStatementAiProviderScore> baselineProviders,
        IReadOnlyList<BillStatementAiProviderFieldScore> baselineProviderFields,
        BillStatementAiShadowReadinessMetrics candidate,
        IReadOnlyList<BillStatementAiFieldScore> candidateFields,
        IReadOnlyList<BillStatementAiProviderScore> candidateProviders,
        IReadOnlyList<BillStatementAiProviderFieldScore> candidateProviderFields)
    {
        ArgumentNullException.ThrowIfNull(
            baselineProviderFields);

        ArgumentNullException.ThrowIfNull(
            candidateProviderFields);

        var aggregateComparison =
            Compare(
                baseline,
                baselineFields,
                baselineProviders,
                candidate,
                candidateFields,
                candidateProviders);

        ValidateProviderFieldScores(
            baselineFields,
            baselineProviders,
            baselineProviderFields,
            nameof(baselineProviderFields));

        ValidateProviderFieldScores(
            candidateFields,
            candidateProviders,
            candidateProviderFields,
            nameof(candidateProviderFields));

        var regressedCount =
            CountRegressedProviderFields(
                baselineProviderFields,
                candidateProviderFields);

        return aggregateComparison with
        {
            CandidateHasNoProviderFieldRegression =
                regressedCount ==
                0,

            RegressedProviderFieldCount =
                regressedCount,

            CandidateQualifiesForPromotionReview =
                aggregateComparison.CandidateHasNoAggregateRegression &&
                aggregateComparison.CandidateHasNoFieldRegression &&
                aggregateComparison.CandidateHasNoProviderRegression &&
                regressedCount ==
                    0 &&
                aggregateComparison.CandidateHasStrictAggregateImprovement
        };
    }

    private static void ValidateProviderFieldScores(
        IReadOnlyList<BillStatementAiFieldScore> aggregateFields,
        IReadOnlyList<BillStatementAiProviderScore> providers,
        IReadOnlyList<BillStatementAiProviderFieldScore> providerFields,
        string parameterName)
    {
        if (providerFields.Count !=
            providers.Count)
        {
            throw new ArgumentException(
                "Prompt comparison requires a fixed field breakdown for every anonymous provider.",
                parameterName);
        }

        var fieldKeys =
            BillStatementAiGroundTruthFieldKeys.All;

        var correctByField =
            new long[fieldKeys.Count];

        var incorrectByField =
            new long[fieldKeys.Count];

        var missedByField =
            new long[fieldKeys.Count];

        for (var providerIndex = 0;
             providerIndex < providers.Count;
             providerIndex++)
        {
            var provider =
                providers[providerIndex];

            var providerField =
                providerFields[providerIndex];

            ArgumentNullException.ThrowIfNull(
                providerField);

            if (providerField.ProviderOrdinal !=
                    provider.ProviderOrdinal ||
                providerField.FieldScores is null ||
                providerField.FieldScores.Count !=
                    fieldKeys.Count)
            {
                throw new ArgumentException(
                    "Anonymous provider field scores have an invalid ordinal or field set.",
                    parameterName);
            }

            long correct =
                0;

            long incorrect =
                0;

            long missed =
                0;

            for (var fieldIndex = 0;
                 fieldIndex < fieldKeys.Count;
                 fieldIndex++)
            {
                var field =
                    providerField.FieldScores[fieldIndex];

                ArgumentNullException.ThrowIfNull(
                    field);

                if (field.FieldKey !=
                        fieldKeys[fieldIndex] ||
                    field.Correct <
                        0 ||
                    field.Incorrect <
                        0 ||
                    field.Missed <
                        0)
                {
                    throw new ArgumentException(
                        "Anonymous provider field scores contain an invalid fixed field.",
                        parameterName);
                }

                correct +=
                    field.Correct;

                incorrect +=
                    field.Incorrect;

                missed +=
                    field.Missed;

                correctByField[fieldIndex] +=
                    field.Correct;

                incorrectByField[fieldIndex] +=
                    field.Incorrect;

                missedByField[fieldIndex] +=
                    field.Missed;
            }

            if (correct !=
                    provider.CorrectFactCount ||
                incorrect !=
                    provider.IncorrectFactCount ||
                missed !=
                    provider.MissedFactCount)
            {
                throw new ArgumentException(
                    "Anonymous provider field scores do not reconcile with provider fact counts.",
                    parameterName);
            }
        }

        var aggregateByKey =
            aggregateFields.ToDictionary(
                field =>
                    field.FieldKey,
                StringComparer.Ordinal);

        for (var index = 0;
             index < fieldKeys.Count;
             index++)
        {
            var aggregate =
                aggregateByKey[fieldKeys[index]];

            if (correctByField[index] !=
                    aggregate.Correct ||
                incorrectByField[index] !=
                    aggregate.Incorrect ||
                missedByField[index] !=
                    aggregate.Missed)
            {
                throw new ArgumentException(
                    "Anonymous provider field scores do not reconcile with fixed field totals.",
                    parameterName);
            }
        }
    }

    private static int CountRegressedProviderFields(
        IReadOnlyList<BillStatementAiProviderFieldScore> baseline,
        IReadOnlyList<BillStatementAiProviderFieldScore> candidate)
    {
        var regressedCount =
            0;

        for (var providerIndex = 0;
             providerIndex < baseline.Count;
             providerIndex++)
        {
            var baselineFields =
                baseline[providerIndex].FieldScores;

            var candidateFields =
                candidate[providerIndex].FieldScores;

            for (var fieldIndex = 0;
                 fieldIndex < baselineFields.Count;
                 fieldIndex++)
            {
                var baselineField =
                    baselineFields[fieldIndex];

                var candidateField =
                    candidateFields[fieldIndex];

                RequireEqual(
                    baselineField.ExpectedFactCount,
                    candidateField.ExpectedFactCount,
                    "anonymous provider fixed-field expected fact count");

                if (candidateField.Correct <
                        baselineField.Correct ||
                    candidateField.Incorrect >
                        baselineField.Incorrect ||
                    candidateField.Missed >
                        baselineField.Missed ||
                    candidateField.Precision <
                        baselineField.Precision ||
                    candidateField.Recall <
                        baselineField.Recall)
                {
                    regressedCount++;
                }
            }
        }

        return regressedCount;
    }

    private static IReadOnlyList<BillStatementAiPromptFieldComparison>
        CompareFields(
            IReadOnlyList<BillStatementAiFieldScore> baselineFields,
            IReadOnlyList<BillStatementAiFieldScore> candidateFields)
    {
        var baselineByKey =
            baselineFields.ToDictionary(
                field =>
                    field.FieldKey,
                StringComparer.Ordinal);

        var candidateByKey =
            candidateFields.ToDictionary(
                field =>
                    field.FieldKey,
                StringComparer.Ordinal);

        var comparisons =
            new List<BillStatementAiPromptFieldComparison>(
                BillStatementAiGroundTruthFieldKeys.All.Count);

        foreach (var fieldKey in
                 BillStatementAiGroundTruthFieldKeys.All)
        {
            var baseline =
                baselineByKey[fieldKey];

            var candidate =
                candidateByKey[fieldKey];

            RequireEqual(
                baseline.ExpectedFactCount,
                candidate.ExpectedFactCount,
                $"{fieldKey} expected fact count");

            var noRegression =
                candidate.Correct >=
                    baseline.Correct &&
                candidate.Incorrect <=
                    baseline.Incorrect &&
                candidate.Missed <=
                    baseline.Missed &&
                candidate.Precision >=
                    baseline.Precision &&
                candidate.Recall >=
                    baseline.Recall;

            comparisons.Add(
                new BillStatementAiPromptFieldComparison(
                    FieldKey:
                        fieldKey,

                    BaselineCorrect:
                        baseline.Correct,

                    CandidateCorrect:
                        candidate.Correct,

                    CorrectDelta:
                        candidate.Correct -
                        baseline.Correct,

                    BaselineIncorrect:
                        baseline.Incorrect,

                    CandidateIncorrect:
                        candidate.Incorrect,

                    IncorrectDelta:
                        candidate.Incorrect -
                        baseline.Incorrect,

                    BaselineMissed:
                        baseline.Missed,

                    CandidateMissed:
                        candidate.Missed,

                    MissedDelta:
                        candidate.Missed -
                        baseline.Missed,

                    BaselinePrecision:
                        baseline.Precision,

                    CandidatePrecision:
                        candidate.Precision,

                    PrecisionDelta:
                        candidate.Precision -
                        baseline.Precision,

                    BaselineRecall:
                        baseline.Recall,

                    CandidateRecall:
                        candidate.Recall,

                    RecallDelta:
                        candidate.Recall -
                        baseline.Recall,

                    CandidateHasNoRegression:
                        noRegression));
        }

        return comparisons.AsReadOnly();
    }

    private static IReadOnlyList<BillStatementAiPromptProviderComparison>
        CompareProviders(
            IReadOnlyList<BillStatementAiProviderScore> baselineProviders,
            IReadOnlyList<BillStatementAiProviderScore> candidateProviders)
    {
        var comparisons =
            new List<BillStatementAiPromptProviderComparison>(
                baselineProviders.Count);

        for (var index = 0;
             index < baselineProviders.Count;
             index++)
        {
            var baseline =
                baselineProviders[index];

            var candidate =
                candidateProviders[index];

            RequireEqual(
                baseline.ProviderOrdinal,
                candidate.ProviderOrdinal,
                "anonymous provider ordinal");

            RequireEqual(
                baseline.StatementCount,
                candidate.StatementCount,
                $"provider {baseline.ProviderOrdinal} statement count");

            RequireEqual(
                baseline.ProviderAttemptCount,
                candidate.ProviderAttemptCount,
                $"provider {baseline.ProviderOrdinal} attempt count");

            RequireEqual(
                baseline.CorrectFactCount +
                    baseline.MissedFactCount,
                candidate.CorrectFactCount +
                    candidate.MissedFactCount,
                $"provider {baseline.ProviderOrdinal} ground-truth fact count");

            var noRegression =
                candidate.CorrectFactCount >=
                    baseline.CorrectFactCount &&
                candidate.IncorrectFactCount <=
                    baseline.IncorrectFactCount &&
                candidate.MissedFactCount <=
                    baseline.MissedFactCount &&
                candidate.FactPrecision >=
                    baseline.FactPrecision &&
                candidate.FactRecall >=
                    baseline.FactRecall &&
                candidate.ReadyCandidateStatementCount >=
                    baseline.ReadyCandidateStatementCount &&
                candidate.ReadyCandidateRate >=
                    baseline.ReadyCandidateRate &&
                candidate.ProviderFailureCount <=
                    baseline.ProviderFailureCount &&
                candidate.ProviderFailureRate <=
                    baseline.ProviderFailureRate &&
                candidate.ScoredDocumentExactMatchCount >=
                    baseline.ScoredDocumentExactMatchCount;

            comparisons.Add(
                new BillStatementAiPromptProviderComparison(
                    ProviderOrdinal:
                        baseline.ProviderOrdinal,

                    StatementCount:
                        baseline.StatementCount,

                    BaselineCorrect:
                        baseline.CorrectFactCount,

                    CandidateCorrect:
                        candidate.CorrectFactCount,

                    CorrectDelta:
                        candidate.CorrectFactCount -
                        baseline.CorrectFactCount,

                    BaselineIncorrect:
                        baseline.IncorrectFactCount,

                    CandidateIncorrect:
                        candidate.IncorrectFactCount,

                    IncorrectDelta:
                        candidate.IncorrectFactCount -
                        baseline.IncorrectFactCount,

                    BaselineMissed:
                        baseline.MissedFactCount,

                    CandidateMissed:
                        candidate.MissedFactCount,

                    MissedDelta:
                        candidate.MissedFactCount -
                        baseline.MissedFactCount,

                    BaselinePrecision:
                        baseline.FactPrecision,

                    CandidatePrecision:
                        candidate.FactPrecision,

                    PrecisionDelta:
                        candidate.FactPrecision -
                        baseline.FactPrecision,

                    BaselineRecall:
                        baseline.FactRecall,

                    CandidateRecall:
                        candidate.FactRecall,

                    RecallDelta:
                        candidate.FactRecall -
                        baseline.FactRecall,

                    BaselineReadyCandidateRate:
                        baseline.ReadyCandidateRate,

                    CandidateReadyCandidateRate:
                        candidate.ReadyCandidateRate,

                    ReadyCandidateRateDelta:
                        candidate.ReadyCandidateRate -
                        baseline.ReadyCandidateRate,

                    BaselineProviderFailureRate:
                        baseline.ProviderFailureRate,

                    CandidateProviderFailureRate:
                        candidate.ProviderFailureRate,

                    ProviderFailureRateDelta:
                        candidate.ProviderFailureRate -
                        baseline.ProviderFailureRate,

                    CandidateHasNoRegression:
                        noRegression)
                {
                    BaselineScoredDocumentExactMatchCount =
                        baseline.ScoredDocumentExactMatchCount,

                    CandidateScoredDocumentExactMatchCount =
                        candidate.ScoredDocumentExactMatchCount,

                    ScoredDocumentExactMatchCountDelta =
                        candidate.ScoredDocumentExactMatchCount -
                        baseline.ScoredDocumentExactMatchCount
                });
        }

        return comparisons.AsReadOnly();
    }

    private static void ValidatePopulation(
        BillStatementAiShadowReadinessMetrics baseline,
        BillStatementAiShadowReadinessMetrics candidate)
    {
        ValidateNonNegative(
            baseline,
            nameof(baseline));

        ValidateNonNegative(
            candidate,
            nameof(candidate));

        if (baseline.EvaluatedStatementCount <=
                0 ||
            baseline.ProviderAttemptCount <=
                0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(baseline),
                "Prompt comparison requires a non-empty evaluated population.");
        }

        RequireEqual(
            baseline.EvaluatedStatementCount,
            candidate.EvaluatedStatementCount,
            nameof(
                BillStatementAiShadowReadinessMetrics
                    .EvaluatedStatementCount));

        RequireEqual(
            baseline.DistinctProviderCount,
            candidate.DistinctProviderCount,
            nameof(
                BillStatementAiShadowReadinessMetrics
                    .DistinctProviderCount));

        RequireEqual(
            baseline.MinimumStatementsForAnyProvider,
            candidate.MinimumStatementsForAnyProvider,
            nameof(
                BillStatementAiShadowReadinessMetrics
                    .MinimumStatementsForAnyProvider));

        RequireEqual(
            baseline.ProviderAttemptCount,
            candidate.ProviderAttemptCount,
            nameof(
                BillStatementAiShadowReadinessMetrics
                    .ProviderAttemptCount));

        RequireEqual(
            baseline.AlertEvaluatedStatementCount,
            candidate.AlertEvaluatedStatementCount,
            nameof(
                BillStatementAiShadowReadinessMetrics
                    .AlertEvaluatedStatementCount));

        var baselineGroundTruthFactCount =
            baseline.CorrectFactCount +
            baseline.MissedFactCount;

        var candidateGroundTruthFactCount =
            candidate.CorrectFactCount +
            candidate.MissedFactCount;

        RequireEqual(
            baselineGroundTruthFactCount,
            candidateGroundTruthFactCount,
            "ground-truth fact count");
    }

    private static void ValidateFieldScores(
        BillStatementAiShadowReadinessMetrics metrics,
        IReadOnlyList<BillStatementAiFieldScore> fields,
        string parameterName)
    {
        if (fields.Count !=
            BillStatementAiGroundTruthFieldKeys.All.Count)
        {
            throw new ArgumentException(
                "Prompt comparison requires the fixed field-score set.",
                parameterName);
        }

        var expectedKeys =
            new HashSet<string>(
                BillStatementAiGroundTruthFieldKeys.All,
                StringComparer.Ordinal);

        var actualKeys =
            new HashSet<string>(
                StringComparer.Ordinal);

        long correct =
            0;

        long incorrect =
            0;

        long missed =
            0;

        foreach (var field in
                 fields)
        {
            ArgumentNullException.ThrowIfNull(
                field);

            if (!expectedKeys.Contains(
                    field.FieldKey) ||
                !actualKeys.Add(
                    field.FieldKey))
            {
                throw new ArgumentException(
                    "Prompt comparison field scores contain an unknown or duplicate field key.",
                    parameterName);
            }

            if (field.Correct <
                    0 ||
                field.Incorrect <
                    0 ||
                field.Missed <
                    0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    "Prompt comparison field-score counts cannot be negative.");
            }

            correct +=
                field.Correct;

            incorrect +=
                field.Incorrect;

            missed +=
                field.Missed;
        }

        if (!actualKeys.SetEquals(
                expectedKeys))
        {
            throw new ArgumentException(
                "Prompt comparison field scores do not contain the fixed field-score set.",
                parameterName);
        }

        if (correct !=
                metrics.CorrectFactCount ||
            incorrect !=
                metrics.IncorrectFactCount ||
            missed !=
                metrics.MissedFactCount)
        {
            throw new ArgumentException(
                "Prompt comparison field scores do not reconcile with aggregate fact counts.",
                parameterName);
        }
    }

    private static void ValidateProviderScores(
        BillStatementAiShadowReadinessMetrics metrics,
        IReadOnlyList<BillStatementAiProviderScore> providers,
        string parameterName)
    {
        if (providers.Count !=
            metrics.DistinctProviderCount)
        {
            throw new ArgumentException(
                "Prompt comparison requires one anonymous provider score per evaluated provider.",
                parameterName);
        }

        long statementCount =
            0;

        long providerAttemptCount =
            0;

        long providerFailureCount =
            0;

        long readyCandidateStatementCount =
            0;

        long correctFactCount =
            0;

        long incorrectFactCount =
            0;

        long missedFactCount =
            0;

        long scoredDocumentExactMatchCount =
            0;

        var expectedOrdinal =
            1;

        foreach (var provider in
                 providers)
        {
            ArgumentNullException.ThrowIfNull(
                provider);

            if (provider.ProviderOrdinal !=
                    expectedOrdinal ||
                provider.StatementCount <=
                    0 ||
                provider.ProviderAttemptCount <
                    0 ||
                provider.ProviderFailureCount <
                    0 ||
                provider.ReadyCandidateStatementCount <
                    0 ||
                provider.CorrectFactCount <
                    0 ||
                provider.IncorrectFactCount <
                    0 ||
                provider.MissedFactCount <
                    0 ||
                provider.ScoredDocumentExactMatchCount <
                    0 ||
                provider.ScoredDocumentExactMatchCount >
                    provider.StatementCount ||
                provider.ProviderAttemptCount >
                    provider.StatementCount ||
                provider.ProviderFailureCount >
                    provider.ProviderAttemptCount ||
                provider.ReadyCandidateStatementCount >
                    provider.ProviderAttemptCount)
            {
                throw new ArgumentException(
                    "Prompt comparison contains an invalid anonymous provider score.",
                    parameterName);
            }

            statementCount +=
                provider.StatementCount;

            providerAttemptCount +=
                provider.ProviderAttemptCount;

            providerFailureCount +=
                provider.ProviderFailureCount;

            readyCandidateStatementCount +=
                provider.ReadyCandidateStatementCount;

            correctFactCount +=
                provider.CorrectFactCount;

            incorrectFactCount +=
                provider.IncorrectFactCount;

            missedFactCount +=
                provider.MissedFactCount;

            scoredDocumentExactMatchCount +=
                provider.ScoredDocumentExactMatchCount;

            expectedOrdinal++;
        }

        if (statementCount !=
                metrics.EvaluatedStatementCount ||
            providerAttemptCount !=
                metrics.ProviderAttemptCount ||
            providerFailureCount !=
                metrics.ProviderFailureCount ||
            readyCandidateStatementCount !=
                metrics.ReadyCandidateStatementCount ||
            correctFactCount !=
                metrics.CorrectFactCount ||
            incorrectFactCount !=
                metrics.IncorrectFactCount ||
            missedFactCount !=
                metrics.MissedFactCount ||
            scoredDocumentExactMatchCount !=
                metrics.ScoredDocumentExactMatchCount)
        {
            throw new ArgumentException(
                "Prompt comparison provider scores do not reconcile with aggregate metrics.",
                parameterName);
        }

        if (providers.Count > 0 &&
            providers.Min(
                provider =>
                    provider.StatementCount) !=
            metrics.MinimumStatementsForAnyProvider)
        {
            throw new ArgumentException(
                "Prompt comparison provider scores do not reconcile with the minimum provider population.",
                parameterName);
        }
    }

    private static void ValidateNonNegative(
        BillStatementAiShadowReadinessMetrics metrics,
        string parameterName)
    {
        var values =
            new[]
            {
                metrics.EvaluatedStatementCount,
                metrics.DistinctProviderCount,
                metrics.MinimumStatementsForAnyProvider,
                metrics.ProviderAttemptCount,
                metrics.ProviderFailureCount,
                metrics.ReadyCandidateStatementCount,
                metrics.CorrectFactCount,
                metrics.IncorrectFactCount,
                metrics.MissedFactCount,
                metrics.AlertEvaluatedStatementCount,
                metrics.FalseAlertStatementCount,
                metrics.ScoredDocumentExactMatchCount
            };

        if (values.Any(
                value =>
                    value <
                    0))
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Prompt-comparison metrics cannot contain negative counts.");
        }

        if (metrics.ProviderFailureCount >
                metrics.ProviderAttemptCount ||
            metrics.ReadyCandidateStatementCount >
                metrics.ProviderAttemptCount ||
            metrics.AlertEvaluatedStatementCount >
                metrics.EvaluatedStatementCount ||
            metrics.FalseAlertStatementCount >
                metrics.AlertEvaluatedStatementCount ||
            metrics.ScoredDocumentExactMatchCount >
                metrics.EvaluatedStatementCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Prompt-comparison metrics contain inconsistent population counts.");
        }
    }

    private static void RequireEqual(
        long baseline,
        long candidate,
        string metricName)
    {
        if (baseline ==
            candidate)
        {
            return;
        }

        throw new ArgumentException(
            $"Prompt comparison requires the same {metricName} for baseline and candidate evaluations.");
    }

    private static decimal Divide(
        long numerator,
        long denominator)
    {
        return denominator ==
                0
            ? 0m
            : decimal.Divide(
                numerator,
                denominator);
    }
}

public sealed record BillStatementAiPromptFieldComparison(
    string FieldKey,
    long BaselineCorrect,
    long CandidateCorrect,
    long CorrectDelta,
    long BaselineIncorrect,
    long CandidateIncorrect,
    long IncorrectDelta,
    long BaselineMissed,
    long CandidateMissed,
    long MissedDelta,
    decimal BaselinePrecision,
    decimal CandidatePrecision,
    decimal PrecisionDelta,
    decimal BaselineRecall,
    decimal CandidateRecall,
    decimal RecallDelta,
    bool CandidateHasNoRegression);

public sealed record BillStatementAiPromptProviderComparison(
    int ProviderOrdinal,
    long StatementCount,
    long BaselineCorrect,
    long CandidateCorrect,
    long CorrectDelta,
    long BaselineIncorrect,
    long CandidateIncorrect,
    long IncorrectDelta,
    long BaselineMissed,
    long CandidateMissed,
    long MissedDelta,
    decimal BaselinePrecision,
    decimal CandidatePrecision,
    decimal PrecisionDelta,
    decimal BaselineRecall,
    decimal CandidateRecall,
    decimal RecallDelta,
    decimal BaselineReadyCandidateRate,
    decimal CandidateReadyCandidateRate,
    decimal ReadyCandidateRateDelta,
    decimal BaselineProviderFailureRate,
    decimal CandidateProviderFailureRate,
    decimal ProviderFailureRateDelta,
    bool CandidateHasNoRegression)
{
    public long BaselineScoredDocumentExactMatchCount { get; init; }

    public long CandidateScoredDocumentExactMatchCount { get; init; }

    public long ScoredDocumentExactMatchCountDelta { get; init; }
}

public sealed record BillStatementAiPromptComparisonResult(
    decimal BaselineFactPrecision,
    decimal CandidateFactPrecision,
    decimal FactPrecisionDelta,
    decimal BaselineFactRecall,
    decimal CandidateFactRecall,
    decimal FactRecallDelta,
    decimal BaselineReadyCandidateRate,
    decimal CandidateReadyCandidateRate,
    decimal ReadyCandidateRateDelta,
    decimal BaselineProviderFailureRate,
    decimal CandidateProviderFailureRate,
    decimal ProviderFailureRateDelta,
    long CorrectFactCountDelta,
    long IncorrectFactCountDelta,
    long MissedFactCountDelta,
    long ReadyCandidateStatementCountDelta,
    long ProviderFailureCountDelta,
    IReadOnlyList<BillStatementAiPromptFieldComparison> FieldComparisons,
    IReadOnlyList<BillStatementAiPromptProviderComparison> ProviderComparisons,
    bool CandidateHasNoAggregateRegression,
    bool CandidateHasNoFieldRegression,
    bool CandidateHasNoProviderRegression,
    bool CandidateHasStrictAggregateImprovement,
    bool CandidateQualifiesForPromotionReview)
{
    public decimal BaselineScoredDocumentExactMatchRate { get; init; }

    public decimal CandidateScoredDocumentExactMatchRate { get; init; }

    public decimal ScoredDocumentExactMatchRateDelta { get; init; }

    public long ScoredDocumentExactMatchCountDelta { get; init; }

    public bool CandidateHasNoScoredDocumentRegression { get; init; }

    public bool CandidateHasNoProviderFieldRegression { get; init; }

    public int RegressedProviderFieldCount { get; init; }

    public IReadOnlyList<string> RegressedFieldKeys =>
        FieldComparisons
            .Where(
                comparison =>
                    !comparison.CandidateHasNoRegression)
            .Select(
                comparison =>
                    comparison.FieldKey)
            .ToArray();

    public IReadOnlyList<int> RegressedProviderOrdinals =>
        ProviderComparisons
            .Where(
                comparison =>
                    !comparison.CandidateHasNoRegression)
            .Select(
                comparison =>
                    comparison.ProviderOrdinal)
            .ToArray();
}

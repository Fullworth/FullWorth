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
        BillStatementAiShadowReadinessMetrics candidate,
        IReadOnlyList<BillStatementAiFieldScore> candidateFields)
    {
        ArgumentNullException.ThrowIfNull(
            baseline);

        ArgumentNullException.ThrowIfNull(
            baselineFields);

        ArgumentNullException.ThrowIfNull(
            candidate);

        ArgumentNullException.ThrowIfNull(
            candidateFields);

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

        var fieldComparisons =
            CompareFields(
                baselineFields,
                candidateFields);

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

        var candidateProviderFailureRate =
            Divide(
                candidate.ProviderFailureCount,
                candidate.ProviderAttemptCount);

        var noAggregateRegression =
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
                baselineProviderFailureRate;

        var noFieldRegression =
            fieldComparisons.All(
                field =>
                    field.CandidateHasNoRegression);

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

            CandidateHasNoAggregateRegression:
                noAggregateRegression,

            CandidateHasNoFieldRegression:
                noFieldRegression,

            CandidateHasStrictAggregateImprovement:
                hasStrictAggregateImprovement,

            CandidateQualifiesForPromotionReview:
                noAggregateRegression &&
                noFieldRegression &&
                hasStrictAggregateImprovement);
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
                metrics.FalseAlertStatementCount
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
                metrics.AlertEvaluatedStatementCount)
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
    bool CandidateHasNoAggregateRegression,
    bool CandidateHasNoFieldRegression,
    bool CandidateHasStrictAggregateImprovement,
    bool CandidateQualifiesForPromotionReview)
{
    public IReadOnlyList<string> RegressedFieldKeys =>
        FieldComparisons
            .Where(
                field =>
                    !field.CandidateHasNoRegression)
            .Select(
                field =>
                    field.FieldKey)
            .ToArray();
}

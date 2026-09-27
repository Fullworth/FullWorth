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
        BillStatementAiShadowReadinessMetrics candidate)
    {
        ArgumentNullException.ThrowIfNull(
            baseline);

        ArgumentNullException.ThrowIfNull(
            candidate);

        ValidatePopulation(
            baseline,
            candidate);

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

            CandidateHasNoAggregateRegression:
                noAggregateRegression,

            CandidateHasStrictAggregateImprovement:
                hasStrictAggregateImprovement,

            CandidateQualifiesForPromotionReview:
                noAggregateRegression &&
                hasStrictAggregateImprovement);
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
    bool CandidateHasNoAggregateRegression,
    bool CandidateHasStrictAggregateImprovement,
    bool CandidateQualifiesForPromotionReview);

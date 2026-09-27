using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiPromptComparisonEvaluatorTests
{
    [Fact]
    public void Compare_ReportsRegressionFreeStrictImprovement()
    {
        var baseline =
            CreateMetrics(
                providerFailureCount:
                    5,
                readyCandidateStatementCount:
                    80,
                correctFactCount:
                    900,
                incorrectFactCount:
                    10,
                missedFactCount:
                    100);

        var candidate =
            CreateMetrics(
                providerFailureCount:
                    3,
                readyCandidateStatementCount:
                    85,
                correctFactCount:
                    930,
                incorrectFactCount:
                    5,
                missedFactCount:
                    70);

        var result =
            new BillStatementAiPromptComparisonEvaluator()
                .Compare(
                    baseline,
                    candidate);

        Assert.True(
            result.CandidateHasNoAggregateRegression);

        Assert.True(
            result.CandidateHasStrictAggregateImprovement);

        Assert.True(
            result.CandidateQualifiesForPromotionReview);

        Assert.True(
            result.FactPrecisionDelta >
            0m);

        Assert.True(
            result.FactRecallDelta >
            0m);

        Assert.Equal(
            0.05m,
            result.ReadyCandidateRateDelta);

        Assert.Equal(
            -0.02m,
            result.ProviderFailureRateDelta);

        Assert.Equal(
            30,
            result.CorrectFactCountDelta);

        Assert.Equal(
            -5,
            result.IncorrectFactCountDelta);

        Assert.Equal(
            -30,
            result.MissedFactCountDelta);
    }

    [Fact]
    public void Compare_FlagsCandidateRegression()
    {
        var baseline =
            CreateMetrics(
                providerFailureCount:
                    2,
                readyCandidateStatementCount:
                    90,
                correctFactCount:
                    950,
                incorrectFactCount:
                    5,
                missedFactCount:
                    50);

        var candidate =
            CreateMetrics(
                providerFailureCount:
                    4,
                readyCandidateStatementCount:
                    88,
                correctFactCount:
                    940,
                incorrectFactCount:
                    10,
                missedFactCount:
                    60);

        var result =
            new BillStatementAiPromptComparisonEvaluator()
                .Compare(
                    baseline,
                    candidate);

        Assert.False(
            result.CandidateHasNoAggregateRegression);

        Assert.False(
            result.CandidateQualifiesForPromotionReview);

        Assert.True(
            result.FactPrecisionDelta <
            0m);

        Assert.True(
            result.FactRecallDelta <
            0m);

        Assert.True(
            result.ReadyCandidateRateDelta <
            0m);

        Assert.True(
            result.ProviderFailureRateDelta >
            0m);
    }

    [Fact]
    public void Compare_RejectsDifferentEvaluationPopulations()
    {
        var baseline =
            CreateMetrics(
                providerFailureCount:
                    0,
                readyCandidateStatementCount:
                    90,
                correctFactCount:
                    900,
                incorrectFactCount:
                    10,
                missedFactCount:
                    100);

        var candidate =
            baseline with
            {
                EvaluatedStatementCount =
                    99,

                ProviderAttemptCount =
                    99,

                ReadyCandidateStatementCount =
                    89
            };

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            baseline,
                            candidate));

        Assert.Contains(
            "same EvaluatedStatementCount",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_RejectsDifferentGroundTruthFactPopulation()
    {
        var baseline =
            CreateMetrics(
                providerFailureCount:
                    0,
                readyCandidateStatementCount:
                    90,
                correctFactCount:
                    900,
                incorrectFactCount:
                    10,
                missedFactCount:
                    100);

        var candidate =
            baseline with
            {
                CorrectFactCount =
                    910
            };

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            baseline,
                            candidate));

        Assert.Contains(
            "same ground-truth fact count",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static BillStatementAiShadowReadinessMetrics CreateMetrics(
        long providerFailureCount,
        long readyCandidateStatementCount,
        long correctFactCount,
        long incorrectFactCount,
        long missedFactCount)
    {
        return new BillStatementAiShadowReadinessMetrics(
            EvaluatedStatementCount:
                100,

            DistinctProviderCount:
                5,

            MinimumStatementsForAnyProvider:
                10,

            ProviderAttemptCount:
                100,

            ProviderFailureCount:
                providerFailureCount,

            ReadyCandidateStatementCount:
                readyCandidateStatementCount,

            CorrectFactCount:
                correctFactCount,

            IncorrectFactCount:
                incorrectFactCount,

            MissedFactCount:
                missedFactCount,

            AlertEvaluatedStatementCount:
                0,

            FalseAlertStatementCount:
                0);
    }
}

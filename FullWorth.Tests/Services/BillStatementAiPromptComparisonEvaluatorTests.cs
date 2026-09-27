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
                    CreateFieldScores(
                        baseline),
                    candidate,
                    CreateFieldScores(
                        candidate));

        Assert.True(
            result.CandidateHasNoAggregateRegression);

        Assert.True(
            result.CandidateHasNoFieldRegression);

        Assert.True(
            result.CandidateHasStrictAggregateImprovement);

        Assert.True(
            result.CandidateQualifiesForPromotionReview);

        Assert.Empty(
            result.RegressedFieldKeys);

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

        Assert.Equal(
            BillStatementAiGroundTruthFieldKeys.All,
            result.FieldComparisons.Select(
                field =>
                    field.FieldKey));
    }

    [Fact]
    public void Compare_FlagsCandidateAggregateRegression()
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
                    CreateFieldScores(
                        baseline),
                    candidate,
                    CreateFieldScores(
                        candidate));

        Assert.False(
            result.CandidateHasNoAggregateRegression);

        Assert.False(
            result.CandidateHasNoFieldRegression);

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
    public void Compare_FieldRegressionVetoesAggregateImprovement()
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
                    920,
                incorrectFactCount:
                    8,
                missedFactCount:
                    80);

        var result =
            new BillStatementAiPromptComparisonEvaluator()
                .Compare(
                    baseline,
                    CreateFieldScores(
                        baseline,
                        totalAmountCorrect:
                            100,
                        totalAmountIncorrect:
                            0,
                        totalAmountMissed:
                            0),
                    candidate,
                    CreateFieldScores(
                        candidate,
                        totalAmountCorrect:
                            99,
                        totalAmountIncorrect:
                            0,
                        totalAmountMissed:
                            1));

        Assert.True(
            result.CandidateHasNoAggregateRegression);

        Assert.True(
            result.CandidateHasStrictAggregateImprovement);

        Assert.False(
            result.CandidateHasNoFieldRegression);

        Assert.False(
            result.CandidateQualifiesForPromotionReview);

        Assert.Equal(
            [
                BillStatementAiGroundTruthFieldKeys.TotalAmount
            ],
            result.RegressedFieldKeys);

        var totalAmount =
            Assert.Single(
                result.FieldComparisons,
                field =>
                    field.FieldKey ==
                    BillStatementAiGroundTruthFieldKeys.TotalAmount);

        Assert.Equal(
            -1,
            totalAmount.CorrectDelta);

        Assert.Equal(
            1,
            totalAmount.MissedDelta);

        Assert.False(
            totalAmount.CandidateHasNoRegression);

        var lineItems =
            Assert.Single(
                result.FieldComparisons,
                field =>
                    field.FieldKey ==
                    BillStatementAiGroundTruthFieldKeys.LineItems);

        Assert.True(
            lineItems.CandidateHasNoRegression);
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
                            CreateFieldScores(
                                baseline),
                            candidate,
                            CreateFieldScores(
                                candidate)));

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
                            CreateFieldScores(
                                baseline),
                            candidate,
                            CreateFieldScores(
                                candidate)));

        Assert.Contains(
            "same ground-truth fact count",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_RejectsDifferentExpectedPopulationForOneField()
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
            baseline;

        var candidateFields =
            CreateFieldScores(
                    candidate,
                    totalAmountCorrect:
                        99,
                    totalAmountIncorrect:
                        0,
                    totalAmountMissed:
                        0)
                .Select(
                    field =>
                        field.FieldKey ==
                            BillStatementAiGroundTruthFieldKeys.LineItems
                            ? field with
                            {
                                Correct =
                                    field.Correct +
                                    1
                            }
                            : field)
                .ToArray();

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            baseline,
                            CreateFieldScores(
                                baseline),
                            candidate,
                            candidateFields));

        Assert.Contains(
            "same TotalAmount expected fact count",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_RejectsFieldScoresThatDoNotReconcileWithAggregateTotals()
    {
        var metrics =
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

        var invalidFields =
            CreateFieldScores(
                    metrics)
                .Select(
                    field =>
                        field.FieldKey ==
                            BillStatementAiGroundTruthFieldKeys.LineItems
                            ? field with
                            {
                                Correct =
                                    field.Correct -
                                    1
                            }
                            : field)
                .ToArray();

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            metrics,
                            invalidFields,
                            metrics,
                            CreateFieldScores(
                                metrics)));

        Assert.Contains(
            "do not reconcile with aggregate fact counts",
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

    private static IReadOnlyList<BillStatementAiFieldScore>
        CreateFieldScores(
            BillStatementAiShadowReadinessMetrics metrics,
            long totalAmountCorrect = 100,
            long totalAmountIncorrect = 0,
            long totalAmountMissed = 0)
    {
        const long FixedScalarCorrectPerField =
            100;

        const int FixedScalarFieldCount =
            5;

        var fixedScalarCorrect =
            FixedScalarCorrectPerField *
            FixedScalarFieldCount;

        var lineItemCorrect =
            metrics.CorrectFactCount -
            totalAmountCorrect -
            fixedScalarCorrect;

        var lineItemIncorrect =
            metrics.IncorrectFactCount -
            totalAmountIncorrect;

        var lineItemMissed =
            metrics.MissedFactCount -
            totalAmountMissed;

        if (lineItemCorrect <
                0 ||
            lineItemIncorrect <
                0 ||
            lineItemMissed <
                0)
        {
            throw new InvalidOperationException(
                "The test metrics cannot be represented by the fixed field-score fixture.");
        }

        return
        [
            new BillStatementAiFieldScore(
                BillStatementAiGroundTruthFieldKeys.TotalAmount,
                totalAmountCorrect,
                totalAmountIncorrect,
                totalAmountMissed),

            FixedScalar(
                BillStatementAiGroundTruthFieldKeys.BillingPeriodStart),

            FixedScalar(
                BillStatementAiGroundTruthFieldKeys.BillingPeriodEnd),

            FixedScalar(
                BillStatementAiGroundTruthFieldKeys.StatementDate),

            FixedScalar(
                BillStatementAiGroundTruthFieldKeys.DueDate),

            FixedScalar(
                BillStatementAiGroundTruthFieldKeys.CurrencyCode),

            new BillStatementAiFieldScore(
                BillStatementAiGroundTruthFieldKeys.LineItems,
                lineItemCorrect,
                lineItemIncorrect,
                lineItemMissed)
        ];

        static BillStatementAiFieldScore FixedScalar(
            string fieldKey)
        {
            return new BillStatementAiFieldScore(
                fieldKey,
                FixedScalarCorrectPerField,
                Incorrect:
                    0,
                Missed:
                    0);
        }
    }
}

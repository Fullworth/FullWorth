using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiPromptComparisonEvaluatorTests
{
    [Fact]
    public void Compare_ReportsRegressionFreeImprovementWithoutIntersectionApproval()
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
                    CreateProviderScores(
                        baseline),
                    candidate,
                    CreateFieldScores(
                        candidate),
                    CreateProviderScores(
                        candidate));

        Assert.True(
            result.CandidateHasNoAggregateRegression);

        Assert.True(
            result.CandidateHasNoFieldRegression);

        Assert.True(
            result.CandidateHasNoProviderRegression);

        Assert.True(
            result.CandidateHasStrictAggregateImprovement);

        Assert.False(
            result.CandidateQualifiesForPromotionReview);

        Assert.False(
            result.CandidateHasNoProviderFieldRegression);

        Assert.Empty(
            result.RegressedFieldKeys);

        Assert.Empty(
            result.RegressedProviderOrdinals);

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
                    CreateProviderScores(
                        baseline),
                    candidate,
                    CreateFieldScores(
                        candidate),
                    CreateProviderScores(
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
                    CreateProviderScores(
                        baseline),
                    candidate,
                    CreateFieldScores(
                        candidate,
                        totalAmountCorrect:
                            99,
                        totalAmountIncorrect:
                            0,
                        totalAmountMissed:
                            1),
                    CreateProviderScores(
                        candidate));

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
    public void Compare_ProviderRegressionVetoesAggregateAndFieldImprovement()
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

        var baselineProviders =
            CreateProviderScores(
                baseline)
                .ToArray();

        var candidateProviders =
            CreateProviderScores(
                candidate)
                .ToArray();

        /*
         * Provider 1 gets worse while provider 2 gets correspondingly better.
         * Aggregate and field totals still improve, so only the provider-aware
         * gate can detect this masked regression.
         */
        candidateProviders[0] =
            candidateProviders[0] with
            {
                CorrectFactCount =
                    baselineProviders[0]
                        .CorrectFactCount -
                    1,

                MissedFactCount =
                    baselineProviders[0]
                        .MissedFactCount +
                    1
            };

        var correctCompensation =
            CreateProviderScores(
                    candidate)[0]
                .CorrectFactCount -
            candidateProviders[0]
                .CorrectFactCount;

        candidateProviders[1] =
            candidateProviders[1] with
            {
                CorrectFactCount =
                    candidateProviders[1]
                        .CorrectFactCount +
                    correctCompensation,

                MissedFactCount =
                    candidateProviders[1]
                        .MissedFactCount -
                    correctCompensation
            };

        var result =
            new BillStatementAiPromptComparisonEvaluator()
                .Compare(
                    baseline,
                    CreateFieldScores(
                        baseline),
                    baselineProviders,
                    candidate,
                    CreateFieldScores(
                        candidate),
                    candidateProviders);

        Assert.True(
            result.CandidateHasNoAggregateRegression);

        Assert.True(
            result.CandidateHasNoFieldRegression);

        Assert.False(
            result.CandidateHasNoProviderRegression);

        Assert.False(
            result.CandidateQualifiesForPromotionReview);

        Assert.Equal(
            new[]
            {
                1
            },
            result.RegressedProviderOrdinals);

        Assert.False(
            result.ProviderComparisons[0]
                .CandidateHasNoRegression);

        Assert.True(
            result.ProviderComparisons[1]
                .CandidateHasNoRegression);
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
                            CreateProviderScores(
                                baseline),
                            candidate,
                            CreateFieldScores(
                                candidate),
                            CreateProviderScores(
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
                            CreateProviderScores(
                                baseline),
                            candidate,
                            CreateFieldScores(
                                candidate),
                            CreateProviderScores(
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
                    0);

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            baseline,
                            CreateFieldScores(
                                baseline),
                            CreateProviderScores(
                                baseline),
                            candidate,
                            candidateFields,
                            CreateProviderScores(
                                candidate)));

        Assert.Contains(
            "same TotalAmount expected fact count",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Compare_RejectsProviderScoresThatDoNotReconcileWithAggregateTotals()
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

        var invalidProviders =
            CreateProviderScores(
                    metrics)
                .ToArray();

        invalidProviders[0] =
            invalidProviders[0] with
            {
                CorrectFactCount =
                    invalidProviders[0]
                        .CorrectFactCount -
                    1
            };

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            metrics,
                            CreateFieldScores(
                                metrics),
                            invalidProviders,
                            metrics,
                            CreateFieldScores(
                                metrics),
                            CreateProviderScores(
                                metrics)));

        Assert.Contains(
            "provider scores do not reconcile with aggregate metrics",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Compare_RejectsDifferentGroundTruthPopulationForAnonymousProvider()
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

        var candidateProviders =
            CreateProviderScores(
                    candidate)
                .ToArray();

        candidateProviders[0] =
            candidateProviders[0] with
            {
                CorrectFactCount =
                    candidateProviders[0]
                        .CorrectFactCount +
                    1
            };

        candidateProviders[1] =
            candidateProviders[1] with
            {
                CorrectFactCount =
                    candidateProviders[1]
                        .CorrectFactCount -
                    1
            };

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .Compare(
                            baseline,
                            CreateFieldScores(
                                baseline),
                            CreateProviderScores(
                                baseline),
                            candidate,
                            CreateFieldScores(
                                candidate),
                            candidateProviders));

        Assert.Contains(
            "provider 1 ground-truth fact count",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
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
                            CreateProviderScores(
                                metrics),
                            metrics,
                            CreateFieldScores(
                                metrics),
                            CreateProviderScores(
                                metrics)));

        Assert.Contains(
            "do not reconcile with aggregate fact counts",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CompareWithProviderFields_GatesMaskedProviderFieldRegression(
        bool regressAtIntersections)
    {
        var baseline =
            CreateCrossMetrics(
                correctFactCount:
                    36);

        var candidate =
            CreateCrossMetrics(
                correctFactCount:
                    38);

        var candidateProviderFields =
            regressAtIntersections
                ? CreateCrossProviderFields(
                    firstTotalCorrect:
                        9,
                    firstDueCorrect:
                        10,
                    secondTotalCorrect:
                        10,
                    secondDueCorrect:
                        9)
                : CreateCrossProviderFields(
                    firstTotalCorrect:
                        10,
                    firstDueCorrect:
                        9,
                    secondTotalCorrect:
                        9,
                    secondDueCorrect:
                        10);

        var result =
            new BillStatementAiPromptComparisonEvaluator()
                .CompareWithProviderFields(
                    baseline,
                    CreateCrossFields(
                        totalCorrect:
                            18,
                        dueCorrect:
                            18),
                    CreateCrossProviders(
                        firstCorrect:
                            18,
                        secondCorrect:
                            18),
                    CreateCrossProviderFields(
                        firstTotalCorrect:
                            10,
                        firstDueCorrect:
                            8,
                        secondTotalCorrect:
                            8,
                        secondDueCorrect:
                            10),
                    candidate,
                    CreateCrossFields(
                        totalCorrect:
                            19,
                        dueCorrect:
                            19),
                    CreateCrossProviders(
                        firstCorrect:
                            19,
                        secondCorrect:
                            19),
                    candidateProviderFields);

        Assert.True(
            result.CandidateHasNoAggregateRegression);

        Assert.True(
            result.CandidateHasNoFieldRegression);

        Assert.True(
            result.CandidateHasNoProviderRegression);

        Assert.True(
            result.CandidateHasStrictAggregateImprovement);

        Assert.Equal(
            regressAtIntersections
                ? 2
                : 0,
            result.RegressedProviderFieldCount);

        Assert.Equal(
            !regressAtIntersections,
            result.CandidateHasNoProviderFieldRegression);

        Assert.Equal(
            !regressAtIntersections,
            result.CandidateQualifiesForPromotionReview);
    }

    [Fact]
    public void CompareWithProviderFields_RejectsChangedTruthInsideBuckets()
    {
        var candidateProviderFields =
            CreateCrossProviderFields(
                    firstTotalCorrect:
                        10,
                    firstDueCorrect:
                        9,
                    secondTotalCorrect:
                        9,
                    secondDueCorrect:
                        10)
                .ToArray();

        candidateProviderFields[0] =
            WithMissed(
                WithMissed(
                    candidateProviderFields[0],
                    BillStatementAiGroundTruthFieldKeys.TotalAmount,
                    1),
                BillStatementAiGroundTruthFieldKeys.DueDate,
                0);

        candidateProviderFields[1] =
            WithMissed(
                WithMissed(
                    candidateProviderFields[1],
                    BillStatementAiGroundTruthFieldKeys.TotalAmount,
                    0),
                BillStatementAiGroundTruthFieldKeys.DueDate,
                1);

        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiPromptComparisonEvaluator()
                        .CompareWithProviderFields(
                            CreateCrossMetrics(
                                correctFactCount:
                                    36),
                            CreateCrossFields(
                                totalCorrect:
                                    18,
                                dueCorrect:
                                    18),
                            CreateCrossProviders(
                                firstCorrect:
                                    18,
                                secondCorrect:
                                    18),
                            CreateCrossProviderFields(
                                firstTotalCorrect:
                                    10,
                                firstDueCorrect:
                                    8,
                                secondTotalCorrect:
                                    8,
                                secondDueCorrect:
                                    10),
                            CreateCrossMetrics(
                                correctFactCount:
                                    38),
                            CreateCrossFields(
                                totalCorrect:
                                    19,
                                dueCorrect:
                                    19),
                            CreateCrossProviders(
                                firstCorrect:
                                    19,
                                secondCorrect:
                                    19),
                            candidateProviderFields));

        Assert.Contains(
            "anonymous provider fixed-field expected fact count",
            exception.Message,
            StringComparison.Ordinal);
    }

    private static BillStatementAiProviderFieldScore WithMissed(
        BillStatementAiProviderFieldScore provider,
        string fieldKey,
        long missed)
    {
        var fields =
            provider.FieldScores.ToArray();

        var index =
            Array.FindIndex(
                fields,
                field =>
                    field.FieldKey ==
                    fieldKey);

        fields[index] =
            fields[index] with
            {
                Missed =
                    missed
            };

        return provider with
        {
            FieldScores =
                fields
        };
    }

    private static BillStatementAiShadowReadinessMetrics CreateCrossMetrics(
        long correctFactCount)
    {
        return new BillStatementAiShadowReadinessMetrics(
            EvaluatedStatementCount:
                20,
            DistinctProviderCount:
                2,
            MinimumStatementsForAnyProvider:
                10,
            ProviderAttemptCount:
                20,
            ProviderFailureCount:
                0,
            ReadyCandidateStatementCount:
                20,
            CorrectFactCount:
                correctFactCount,
            IncorrectFactCount:
                0,
            MissedFactCount:
                40 -
                correctFactCount,
            AlertEvaluatedStatementCount:
                0,
            FalseAlertStatementCount:
                0);
    }

    private static IReadOnlyList<BillStatementAiFieldScore> CreateCrossFields(
        long totalCorrect,
        long dueCorrect)
    {
        return BillStatementAiGroundTruthFieldKeys.All
            .Select(
                fieldKey =>
                    fieldKey switch
                    {
                        BillStatementAiGroundTruthFieldKeys.TotalAmount =>
                            new BillStatementAiFieldScore(
                                fieldKey,
                                totalCorrect,
                                0,
                                20 - totalCorrect),

                        BillStatementAiGroundTruthFieldKeys.DueDate =>
                            new BillStatementAiFieldScore(
                                fieldKey,
                                dueCorrect,
                                0,
                                20 - dueCorrect),

                        _ =>
                            new BillStatementAiFieldScore(
                                fieldKey,
                                0,
                                0,
                                0)
                    })
            .ToArray();
    }

    private static IReadOnlyList<BillStatementAiProviderScore> CreateCrossProviders(
        long firstCorrect,
        long secondCorrect)
    {
        return
        [
            CreateProvider(
                1,
                firstCorrect),
            CreateProvider(
                2,
                secondCorrect)
        ];

        static BillStatementAiProviderScore CreateProvider(
            int ordinal,
            long correct)
        {
            return new BillStatementAiProviderScore(
                ProviderOrdinal:
                    ordinal,
                StatementCount:
                    10,
                ProviderAttemptCount:
                    10,
                ProviderFailureCount:
                    0,
                ReadyCandidateStatementCount:
                    10,
                CorrectFactCount:
                    correct,
                IncorrectFactCount:
                    0,
                MissedFactCount:
                    20 - correct);
        }
    }

    private static IReadOnlyList<BillStatementAiProviderFieldScore>
        CreateCrossProviderFields(
            long firstTotalCorrect,
            long firstDueCorrect,
            long secondTotalCorrect,
            long secondDueCorrect)
    {
        return
        [
            CreateProviderFields(
                1,
                firstTotalCorrect,
                firstDueCorrect),
            CreateProviderFields(
                2,
                secondTotalCorrect,
                secondDueCorrect)
        ];

        static BillStatementAiProviderFieldScore CreateProviderFields(
            int ordinal,
            long totalCorrect,
            long dueCorrect)
        {
            return new BillStatementAiProviderFieldScore(
                ProviderOrdinal:
                    ordinal,

                FieldScores:
                    BillStatementAiGroundTruthFieldKeys.All
                        .Select(
                            fieldKey =>
                                fieldKey switch
                                {
                                    BillStatementAiGroundTruthFieldKeys.TotalAmount =>
                                        new BillStatementAiFieldScore(
                                            fieldKey,
                                            totalCorrect,
                                            0,
                                            10 - totalCorrect),

                                    BillStatementAiGroundTruthFieldKeys.DueDate =>
                                        new BillStatementAiFieldScore(
                                            fieldKey,
                                            dueCorrect,
                                            0,
                                            10 - dueCorrect),

                                    _ =>
                                        new BillStatementAiFieldScore(
                                            fieldKey,
                                            0,
                                            0,
                                            0)
                                })
                        .ToArray());
        }
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

    private static IReadOnlyList<BillStatementAiProviderScore>
        CreateProviderScores(
            BillStatementAiShadowReadinessMetrics metrics)
    {
        var providerCount =
            checked(
                (int)metrics.DistinctProviderCount);

        if (providerCount <=
            0)
        {
            throw new InvalidOperationException(
                "The provider-score fixture requires at least one provider.");
        }

        var statementCounts =
            new long[providerCount];

        statementCounts[0] =
            metrics.MinimumStatementsForAnyProvider;

        var remainingStatements =
            metrics.EvaluatedStatementCount -
            statementCounts[0];

        for (var index = 1;
             index < providerCount;
             index++)
        {
            var remainingBuckets =
                providerCount -
                index;

            var value =
                remainingBuckets ==
                    0
                    ? remainingStatements
                    : remainingStatements /
                        remainingBuckets;

            statementCounts[index] =
                value;

            remainingStatements -=
                value;
        }

        if (statementCounts.Any(
                count =>
                    count <
                    metrics.MinimumStatementsForAnyProvider) ||
            statementCounts.Sum() !=
                metrics.EvaluatedStatementCount ||
            metrics.ProviderAttemptCount !=
                metrics.EvaluatedStatementCount)
        {
            throw new InvalidOperationException(
                "The test metrics cannot be represented by the provider-score fixture.");
        }

        var readyCounts =
            DistributeWithCapacity(
                metrics.ReadyCandidateStatementCount,
                statementCounts);

        var failureCounts =
            DistributeWithCapacity(
                metrics.ProviderFailureCount,
                statementCounts);

        var correctCounts =
            DistributeEvenly(
                metrics.CorrectFactCount,
                providerCount);

        var incorrectCounts =
            DistributeEvenly(
                metrics.IncorrectFactCount,
                providerCount);

        var missedCounts =
            DistributeEvenly(
                metrics.MissedFactCount,
                providerCount);

        return Enumerable.Range(
                0,
                providerCount)
            .Select(
                index =>
                    new BillStatementAiProviderScore(
                        ProviderOrdinal:
                            index + 1,

                        StatementCount:
                            statementCounts[index],

                        ProviderAttemptCount:
                            statementCounts[index],

                        ProviderFailureCount:
                            failureCounts[index],

                        ReadyCandidateStatementCount:
                            readyCounts[index],

                        CorrectFactCount:
                            correctCounts[index],

                        IncorrectFactCount:
                            incorrectCounts[index],

                        MissedFactCount:
                            missedCounts[index]))
            .ToArray();

        static long[] DistributeEvenly(
            long total,
            int bucketCount)
        {
            var values =
                new long[bucketCount];

            for (var index = 0;
                 index < bucketCount;
                 index++)
            {
                values[index] =
                    total /
                    bucketCount +
                    (index <
                        total %
                        bucketCount
                        ? 1
                        : 0);
            }

            return values;
        }

        static long[] DistributeWithCapacity(
            long total,
            IReadOnlyList<long> capacities)
        {
            var values =
                new long[capacities.Count];

            var remaining =
                total;

            for (var index = 0;
                 index < capacities.Count;
                 index++)
            {
                var value =
                    Math.Min(
                        remaining,
                        capacities[index]);

                values[index] =
                    value;

                remaining -=
                    value;
            }

            if (remaining !=
                0)
            {
                throw new InvalidOperationException(
                    "The test metric exceeds provider capacity.");
            }

            return values;
        }
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

        BillStatementAiFieldScore FixedScalar(
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

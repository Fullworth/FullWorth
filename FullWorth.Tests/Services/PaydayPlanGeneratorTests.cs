using FullWorth.Core.Models.Planning;
using FullWorth.Core.Services;

namespace FullWorth.Tests.Services;

public sealed class PaydayPlanGeneratorTests
{
    [Fact]
    public void Generate_AggregatesRecommendationsAcrossBills()
    {
        var result =
            PaydayPlanGenerator.Generate(
                new PaydayPlanRequest(
                    PaycheckAmount: 1000m,
                    CurrentPayDate:
                        new DateOnly(
                            2026,
                            11,
                            20),
                    CurrencyCode: "usd",
                    Schedule:
                        BiweeklySchedule(),
                    Bills:
                    [
                        Bill(
                            "Internet",
                            600m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                18)),
                        Bill(
                            "Electricity",
                            200m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                18))
                    ]));

        Assert.Equal(
            "USD",
            result.CurrencyCode);

        Assert.Equal(
            400m,
            result.RecommendedSetAside);

        Assert.Equal(
            600m,
            result.PaycheckRemainingAfterPlan);

        Assert.Equal(
            0m,
            result.Shortfall);

        Assert.All(
            result.Bills,
            bill =>
                Assert.Equal(
                    BillFundingWindowStatus.Active,
                    bill.Status));
    }

    [Fact]
    public void Generate_SurfacesPaycheckLevelShortfall()
    {
        var result =
            PaydayPlanGenerator.Generate(
                new PaydayPlanRequest(
                    PaycheckAmount: 800m,
                    CurrentPayDate:
                        new DateOnly(
                            2026,
                            11,
                            20),
                    CurrencyCode: "USD",
                    Schedule:
                        BiweeklySchedule(),
                    Bills:
                    [
                        Bill(
                            "Past due",
                            700m,
                            0m,
                            new DateOnly(
                                2026,
                                11,
                                19)),
                        Bill(
                            "Upcoming",
                            600m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                18))
                    ]));

        Assert.Equal(
            1000m,
            result.RecommendedSetAside);

        Assert.Equal(
            0m,
            result.PaycheckRemainingAfterPlan);

        Assert.Equal(
            200m,
            result.Shortfall);

        Assert.Equal(
            BillFundingWindowStatus.DueOrPast,
            result.Bills[0].Status);
    }

    [Fact]
    public void Generate_DoesNotFundBillsOutsideConfiguredWindow()
    {
        var result =
            PaydayPlanGenerator.Generate(
                new PaydayPlanRequest(
                    PaycheckAmount: 1200m,
                    CurrentPayDate:
                        new DateOnly(
                            2026,
                            10,
                            23),
                    CurrencyCode: "USD",
                    Schedule:
                        BiweeklySchedule(
                            new DateOnly(
                                2026,
                                10,
                                23)),
                    Bills:
                    [
                        Bill(
                            "Later bill",
                            720m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                18),
                            paychecksAhead:
                                3)
                    ]));

        var bill =
            Assert.Single(
                result.Bills);

        Assert.Equal(
            BillFundingWindowStatus.NotInFundingWindow,
            bill.Status);

        Assert.Equal(
            0m,
            result.RecommendedSetAside);
    }

    [Fact]
    public void Generate_UsesPreviouslyPlannedAmount()
    {
        var result =
            PaydayPlanGenerator.Generate(
                new PaydayPlanRequest(
                    PaycheckAmount: 1000m,
                    CurrentPayDate:
                        new DateOnly(
                            2026,
                            11,
                            20),
                    CurrencyCode: "USD",
                    Schedule:
                        BiweeklySchedule(),
                    Bills:
                    [
                        Bill(
                            "Internet",
                            500m,
                            100m,
                            new DateOnly(
                                2026,
                                12,
                                18))
                    ]));

        var bill =
            Assert.Single(
                result.Bills);

        Assert.Equal(
            400m,
            bill.RemainingAmount);

        Assert.Equal(
            200m,
            bill.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void Generate_RejectsCrossCurrencyArithmetic()
    {
        var request =
            new PaydayPlanRequest(
                PaycheckAmount: 1000m,
                CurrentPayDate:
                    new DateOnly(
                        2026,
                        11,
                        20),
                CurrencyCode: "USD",
                Schedule:
                    BiweeklySchedule(),
                Bills:
                [
                    Bill(
                        "Canadian bill",
                        100m,
                        0m,
                        new DateOnly(
                            2026,
                            12,
                            18),
                        currencyCode:
                            "CAD")
                ]);

        Assert.Throws<InvalidOperationException>(
            () =>
                PaydayPlanGenerator.Generate(
                    request));
    }

    [Fact]
    public void Generate_OrdersBillsByDueDateThenProvider()
    {
        var laterId =
            Guid.NewGuid();

        var betaId =
            Guid.NewGuid();

        var alphaId =
            Guid.NewGuid();

        var result =
            PaydayPlanGenerator.Generate(
                new PaydayPlanRequest(
                    PaycheckAmount: 1500m,
                    CurrentPayDate:
                        new DateOnly(
                            2026,
                            11,
                            20),
                    CurrencyCode: "USD",
                    Schedule:
                        BiweeklySchedule(),
                    Bills:
                    [
                        Bill(
                            "Later",
                            100m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                20),
                            billStreamId:
                                laterId),
                        Bill(
                            "Beta",
                            100m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                18),
                            billStreamId:
                                betaId),
                        Bill(
                            "Alpha",
                            100m,
                            0m,
                            new DateOnly(
                                2026,
                                12,
                                18),
                            billStreamId:
                                alphaId)
                    ]));

        Assert.Equal(
            [
                alphaId,
                betaId,
                laterId
            ],
            result.Bills
                .Select(
                    bill =>
                        bill.BillStreamId));
    }

    [Fact]
    public void Generate_RejectsFractionalCentPaycheck()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                PaydayPlanGenerator.Generate(
                    new PaydayPlanRequest(
                        PaycheckAmount:
                            1000.001m,
                        CurrentPayDate:
                            new DateOnly(
                                2026,
                                11,
                                20),
                        CurrencyCode:
                            "USD",
                        Schedule:
                            BiweeklySchedule(),
                        Bills:
                            [])));
    }

    private static PayScheduleDefinition BiweeklySchedule(
        DateOnly? anchorPayDate = null) =>
        new(
            PayScheduleFrequency.Biweekly,
            anchorPayDate ??
                new DateOnly(
                    2026,
                    11,
                    6));

    private static PaydayPlanBillInput Bill(
        string providerName,
        decimal amountDue,
        decimal alreadyPlanned,
        DateOnly dueDate,
        int paychecksAhead = 3,
        string currencyCode = "USD",
        Guid? billStreamId = null) =>
        new(
            billStreamId ??
                Guid.NewGuid(),
            providerName,
            amountDue,
            alreadyPlanned,
            dueDate,
            paychecksAhead,
            currencyCode);
}

using FullWorth.Core.Services;

namespace FullWorth.Tests.Services;

public sealed class PaycheckBillPlanCalculatorTests
{
    [Fact]
    public void BiweeklyPlan_StartsAtConfiguredPaycheckDistance()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 720m,
                    AlreadySetAside: 0m,
                    DueDate: new DateOnly(2026, 12, 18),
                    CurrentPayDate: new DateOnly(2026, 10, 23),
                    PaychecksAhead: 3,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Biweekly,
                        new DateOnly(2026, 10, 23))));

        Assert.Equal(
            BillFundingWindowStatus.NotInFundingWindow,
            result.Status);

        Assert.Equal(
            4,
            result.PaychecksRemaining);

        Assert.Equal(
            0m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void BiweeklyPlan_AllocatesExactlyAcrossThreePaychecks()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 720m,
                    AlreadySetAside: 0m,
                    DueDate: new DateOnly(2026, 12, 18),
                    CurrentPayDate: new DateOnly(2026, 11, 20),
                    PaychecksAhead: 3,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Biweekly,
                        new DateOnly(2026, 11, 6))));

        Assert.Equal(
            BillFundingWindowStatus.Active,
            result.Status);

        Assert.Equal(
            2,
            result.PaychecksRemaining);

        Assert.Equal(
            360m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void Recalculation_UsesOnlyRemainingAmount()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 780m,
                    AlreadySetAside: 240m,
                    DueDate: new DateOnly(2026, 12, 18),
                    CurrentPayDate: new DateOnly(2026, 11, 20),
                    PaychecksAhead: 3,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Biweekly,
                        new DateOnly(2026, 11, 6))));

        Assert.Equal(
            540m,
            result.RemainingAmount);

        Assert.Equal(
            270m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void CentRemainder_IsAssignedWithoutUnderfunding()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 100.01m,
                    AlreadySetAside: 0m,
                    DueDate: new DateOnly(2026, 11, 30),
                    CurrentPayDate: new DateOnly(2026, 11, 2),
                    PaychecksAhead: 3,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Biweekly,
                        new DateOnly(2026, 11, 2))));

        Assert.Equal(
            2,
            result.PaychecksRemaining);

        Assert.Equal(
            50.01m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void SemiMonthlyPlan_ClampsEndOfMonthAndDeduplicatesDates()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 300m,
                    AlreadySetAside: 0m,
                    DueDate: new DateOnly(2027, 3, 20),
                    CurrentPayDate: new DateOnly(2027, 2, 28),
                    PaychecksAhead: 4,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.SemiMonthly,
                        new DateOnly(2027, 1, 31),
                        SecondaryDayOfMonth: 15)));

        Assert.Equal(
            BillFundingWindowStatus.Active,
            result.Status);

        Assert.Equal(
            new[]
            {
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 3, 15)
            },
            result.RemainingPayDates);

        Assert.Equal(
            150m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void MonthlyPlan_ClampsToLastDayOfShortMonth()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 200m,
                    AlreadySetAside: 0m,
                    DueDate: new DateOnly(2027, 4, 15),
                    CurrentPayDate: new DateOnly(2027, 2, 28),
                    PaychecksAhead: 3,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Monthly,
                        new DateOnly(2027, 1, 31))));

        Assert.Equal(
            new[]
            {
                new DateOnly(2027, 2, 28),
                new DateOnly(2027, 3, 31)
            },
            result.RemainingPayDates);
    }

    [Fact]
    public void PaycheckOnDueDate_IsNotCountedAsFundingPaycheck()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 125m,
                    AlreadySetAside: 0m,
                    DueDate: new DateOnly(2026, 10, 2),
                    CurrentPayDate: new DateOnly(2026, 10, 2),
                    PaychecksAhead: 2,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Weekly,
                        new DateOnly(2026, 10, 2))));

        Assert.Equal(
            BillFundingWindowStatus.DueOrPast,
            result.Status);

        Assert.Equal(
            125m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void FullyPlannedBill_HasNoAdditionalRecommendation()
    {
        var result =
            PaycheckBillPlanCalculator.Calculate(
                new PaycheckBillPlanRequest(
                    AmountDue: 500m,
                    AlreadySetAside: 500m,
                    DueDate: new DateOnly(2026, 11, 20),
                    CurrentPayDate: new DateOnly(2026, 10, 23),
                    PaychecksAhead: 3,
                    Schedule: new PayScheduleDefinition(
                        PayScheduleFrequency.Biweekly,
                        new DateOnly(2026, 10, 23))));

        Assert.Equal(
            BillFundingWindowStatus.FullyPlanned,
            result.Status);

        Assert.Equal(
            0m,
            result.RecommendedSetAsideFromCurrentPaycheck);
    }

    [Fact]
    public void RejectsFractionsOfCent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                PaycheckBillPlanCalculator.Calculate(
                    new PaycheckBillPlanRequest(
                        AmountDue: 10.001m,
                        AlreadySetAside: 0m,
                        DueDate: new DateOnly(2026, 11, 20),
                        CurrentPayDate: new DateOnly(2026, 11, 6),
                        PaychecksAhead: 2,
                        Schedule: new PayScheduleDefinition(
                            PayScheduleFrequency.Biweekly,
                            new DateOnly(2026, 11, 6)))));
    }
}

using FullWorth.Core.Models.Planning;

namespace FullWorth.Core.Services;

public enum BillFundingWindowStatus
{
    NotInFundingWindow = 1,
    Active = 2,
    DueOrPast = 3,
    FullyPlanned = 4
}

public sealed record PayScheduleDefinition(
    PayScheduleFrequency Frequency,
    DateOnly AnchorPayDate,
    int? SecondaryDayOfMonth = null);

public sealed record PaycheckBillPlanRequest(
    decimal AmountDue,
    decimal AlreadySetAside,
    DateOnly DueDate,
    DateOnly CurrentPayDate,
    int PaychecksAhead,
    PayScheduleDefinition Schedule);

public sealed record PaycheckBillPlanResult(
    BillFundingWindowStatus Status,
    decimal AmountDue,
    decimal AlreadySetAside,
    decimal RemainingAmount,
    int PaychecksRemaining,
    decimal RecommendedSetAsideFromCurrentPaycheck,
    IReadOnlyList<DateOnly> RemainingPayDates);

public static class PaycheckBillPlanCalculator
{
    private const int MaximumPaychecksAhead = 26;

    public static PaycheckBillPlanResult Calculate(
        PaycheckBillPlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Schedule);

        ValidateMoney(
            request.AmountDue,
            nameof(request.AmountDue));

        ValidateMoney(
            request.AlreadySetAside,
            nameof(request.AlreadySetAside));

        if (request.AlreadySetAside >
            request.AmountDue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.AlreadySetAside),
                "Already-set-aside amount cannot exceed the amount due.");
        }

        if (request.PaychecksAhead is < 1 or > MaximumPaychecksAhead)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request.PaychecksAhead),
                $"Paychecks ahead must be between 1 and {MaximumPaychecksAhead}.");
        }

        ValidateSchedule(
            request.Schedule);

        var amountDue =
            RoundMoney(request.AmountDue);

        var alreadySetAside =
            RoundMoney(request.AlreadySetAside);

        var remaining =
            amountDue -
            alreadySetAside;

        if (remaining <= 0m)
        {
            return new PaycheckBillPlanResult(
                BillFundingWindowStatus.FullyPlanned,
                amountDue,
                alreadySetAside,
                0m,
                0,
                0m,
                Array.Empty<DateOnly>());
        }

        /*
         * Do not assume money arriving on the same calendar day as a bill's
         * due date is available before the bill is collected. The planning
         * window therefore uses paydays strictly before the due date.
         */
        if (request.CurrentPayDate >=
            request.DueDate)
        {
            return new PaycheckBillPlanResult(
                BillFundingWindowStatus.DueOrPast,
                amountDue,
                alreadySetAside,
                remaining,
                0,
                remaining,
                Array.Empty<DateOnly>());
        }

        var remainingPayDates =
            BuildRemainingPayDates(
                request.Schedule,
                request.CurrentPayDate,
                request.DueDate);

        /*
         * The current event represents an observed/confirmed payday, even if
         * it arrived a day early or late relative to the configured cadence.
         */
        if (!remainingPayDates.Contains(
                request.CurrentPayDate))
        {
            remainingPayDates.Insert(
                0,
                request.CurrentPayDate);
        }

        remainingPayDates =
            remainingPayDates
                .Where(
                    payDate =>
                        payDate >= request.CurrentPayDate &&
                        payDate < request.DueDate)
                .Distinct()
                .OrderBy(payDate => payDate)
                .ToList();

        var paychecksRemaining =
            remainingPayDates.Count;

        if (paychecksRemaining == 0)
        {
            return new PaycheckBillPlanResult(
                BillFundingWindowStatus.DueOrPast,
                amountDue,
                alreadySetAside,
                remaining,
                0,
                remaining,
                Array.Empty<DateOnly>());
        }

        if (paychecksRemaining >
            request.PaychecksAhead)
        {
            return new PaycheckBillPlanResult(
                BillFundingWindowStatus.NotInFundingWindow,
                amountDue,
                alreadySetAside,
                remaining,
                paychecksRemaining,
                0m,
                remainingPayDates);
        }

        var currentContribution =
            AllocateFirstContribution(
                remaining,
                paychecksRemaining);

        return new PaycheckBillPlanResult(
            BillFundingWindowStatus.Active,
            amountDue,
            alreadySetAside,
            remaining,
            paychecksRemaining,
            currentContribution,
            remainingPayDates);
    }

    private static List<DateOnly> BuildRemainingPayDates(
        PayScheduleDefinition schedule,
        DateOnly currentPayDate,
        DateOnly dueDate)
    {
        /*
         * CurrentPayDate is an observed/confirmed paycheck. It represents the
         * current pay cycle even when payroll posted a little early or late.
         *
         * First align that observation to the nearest configured schedule
         * occurrence. Future configured dates are then generated strictly
         * after that occurrence so an early paycheck is not double-counted
         * together with the scheduled date it replaced.
         */
        var scheduledCurrentPayDate =
            FindScheduledPayDateForObservedPaycheck(
                schedule,
                currentPayDate);

        var results =
            BuildScheduledPayDatesAfter(
                schedule,
                scheduledCurrentPayDate,
                dueDate)
                .Where(
                    payDate =>
                        payDate >
                            currentPayDate &&
                        payDate <
                            dueDate)
                .ToList();

        results.Insert(
            0,
            currentPayDate);

        return results
            .Distinct()
            .OrderBy(
                payDate =>
                    payDate)
            .ToList();
    }

    private static DateOnly FindScheduledPayDateForObservedPaycheck(
        PayScheduleDefinition schedule,
        DateOnly currentPayDate)
    {
        return schedule.Frequency switch
        {
            PayScheduleFrequency.Weekly =>
                FindNearestFixedIntervalPayDate(
                    schedule.AnchorPayDate,
                    currentPayDate,
                    7),

            PayScheduleFrequency.Biweekly =>
                FindNearestFixedIntervalPayDate(
                    schedule.AnchorPayDate,
                    currentPayDate,
                    14),

            PayScheduleFrequency.Monthly =>
                FindNearestMonthlyPayDate(
                    schedule.AnchorPayDate.Day,
                    secondaryDayOfMonth:
                        null,
                    currentPayDate),

            PayScheduleFrequency.SemiMonthly =>
                FindNearestMonthlyPayDate(
                    schedule.AnchorPayDate.Day,
                    schedule.SecondaryDayOfMonth,
                    currentPayDate),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(schedule),
                    "Pay schedule frequency is invalid.")
        };
    }

    private static DateOnly FindNearestFixedIntervalPayDate(
        DateOnly anchorPayDate,
        DateOnly currentPayDate,
        int intervalDays)
    {
        var daysFromAnchor =
            currentPayDate.DayNumber -
            anchorPayDate.DayNumber;

        var intervalOffset =
            (int)Math.Floor(
                daysFromAnchor /
                (double)intervalDays);

        var previous =
            anchorPayDate.AddDays(
                intervalOffset *
                intervalDays);

        var next =
            previous.AddDays(
                intervalDays);

        var previousDistance =
            Math.Abs(
                currentPayDate.DayNumber -
                previous.DayNumber);

        var nextDistance =
            Math.Abs(
                next.DayNumber -
                currentPayDate.DayNumber);

        /*
         * A tie chooses the earlier scheduled occurrence. That treats an
         * exactly-midpoint observation as a late current-cycle paycheck rather
         * than an early future-cycle paycheck.
         */
        return previousDistance <=
            nextDistance
                ? previous
                : next;
    }

    private static DateOnly FindNearestMonthlyPayDate(
        int primaryDayOfMonth,
        int? secondaryDayOfMonth,
        DateOnly currentPayDate)
    {
        var month =
            new DateOnly(
                currentPayDate.Year,
                currentPayDate.Month,
                1);

        var candidates =
            new List<DateOnly>();

        for (var offset = -1;
             offset <= 1;
             offset++)
        {
            var candidateMonth =
                month.AddMonths(
                    offset);

            AddMonthlyCandidates(
                candidates,
                candidateMonth,
                primaryDayOfMonth,
                secondaryDayOfMonth);
        }

        return candidates
            .Distinct()
            .OrderBy(
                candidate =>
                    Math.Abs(
                        candidate.DayNumber -
                        currentPayDate.DayNumber))
            .ThenBy(
                candidate =>
                    candidate)
            .First();
    }

    private static List<DateOnly> BuildScheduledPayDatesAfter(
        PayScheduleDefinition schedule,
        DateOnly scheduledCurrentPayDate,
        DateOnly dueDate)
    {
        return schedule.Frequency switch
        {
            PayScheduleFrequency.Weekly =>
                BuildFixedIntervalPayDatesAfter(
                    scheduledCurrentPayDate,
                    dueDate,
                    7),

            PayScheduleFrequency.Biweekly =>
                BuildFixedIntervalPayDatesAfter(
                    scheduledCurrentPayDate,
                    dueDate,
                    14),

            PayScheduleFrequency.Monthly =>
                BuildMonthlyPayDatesAfter(
                    schedule.AnchorPayDate.Day,
                    secondaryDayOfMonth:
                        null,
                    scheduledCurrentPayDate,
                    dueDate),

            PayScheduleFrequency.SemiMonthly =>
                BuildMonthlyPayDatesAfter(
                    schedule.AnchorPayDate.Day,
                    schedule.SecondaryDayOfMonth,
                    scheduledCurrentPayDate,
                    dueDate),

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(schedule),
                    "Pay schedule frequency is invalid.")
        };
    }

    private static List<DateOnly> BuildFixedIntervalPayDatesAfter(
        DateOnly scheduledCurrentPayDate,
        DateOnly dueDate,
        int intervalDays)
    {
        var results =
            new List<DateOnly>();

        for (var candidate =
                 scheduledCurrentPayDate.AddDays(
                     intervalDays);
             candidate <
                 dueDate;
             candidate =
                 candidate.AddDays(
                     intervalDays))
        {
            results.Add(
                candidate);
        }

        return results;
    }

    private static List<DateOnly> BuildMonthlyPayDatesAfter(
        int primaryDayOfMonth,
        int? secondaryDayOfMonth,
        DateOnly scheduledCurrentPayDate,
        DateOnly dueDate)
    {
        var results =
            new List<DateOnly>();

        var month =
            new DateOnly(
                scheduledCurrentPayDate.Year,
                scheduledCurrentPayDate.Month,
                1);

        var finalMonth =
            new DateOnly(
                dueDate.Year,
                dueDate.Month,
                1);

        while (month <=
               finalMonth)
        {
            var candidates =
                new List<DateOnly>();

            AddMonthlyCandidates(
                candidates,
                month,
                primaryDayOfMonth,
                secondaryDayOfMonth);

            foreach (var candidate in
                     candidates)
            {
                if (candidate >
                        scheduledCurrentPayDate &&
                    candidate <
                        dueDate)
                {
                    results.Add(
                        candidate);
                }
            }

            month =
                month.AddMonths(
                    1);
        }

        return results
            .Distinct()
            .OrderBy(
                date =>
                    date)
            .ToList();
    }

    private static void AddMonthlyCandidates(
        ICollection<DateOnly> candidates,
        DateOnly month,
        int primaryDayOfMonth,
        int? secondaryDayOfMonth)
    {
        AddMonthlyCandidate(
            candidates,
            month,
            primaryDayOfMonth);

        if (secondaryDayOfMonth.HasValue)
        {
            AddMonthlyCandidate(
                candidates,
                month,
                secondaryDayOfMonth.Value);
        }
    }

    private static void AddMonthlyCandidate(
        ICollection<DateOnly> candidates,
        DateOnly month,
        int configuredDay)
    {
        var actualDay =
            Math.Min(
                configuredDay,
                DateTime.DaysInMonth(
                    month.Year,
                    month.Month));

        candidates.Add(
            new DateOnly(
                month.Year,
                month.Month,
                actualDay));
    }

    private static decimal AllocateFirstContribution(
        decimal remainingAmount,
        int paychecksRemaining)
    {
        var totalCents =
            checked(
                (long)decimal.Round(
                    remainingAmount * 100m,
                    0,
                    MidpointRounding.AwayFromZero));

        var baseCents =
            totalCents /
            paychecksRemaining;

        var remainderCents =
            totalCents %
            paychecksRemaining;

        var currentCents =
            baseCents +
            (remainderCents > 0
                ? 1
                : 0);

        return currentCents /
               100m;
    }

    private static void ValidateSchedule(
        PayScheduleDefinition schedule)
    {
        if (!Enum.IsDefined(
                schedule.Frequency))
        {
            throw new ArgumentOutOfRangeException(
                nameof(schedule),
                "Pay schedule frequency is invalid.");
        }

        if (schedule.Frequency ==
            PayScheduleFrequency.SemiMonthly)
        {
            if (schedule.SecondaryDayOfMonth is < 1 or > 31)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(schedule),
                    "Semi-monthly schedules require a second day between 1 and 31.");
            }

            if (schedule.SecondaryDayOfMonth ==
                schedule.AnchorPayDate.Day)
            {
                throw new ArgumentException(
                    "Semi-monthly pay days must be different.",
                    nameof(schedule));
            }
        }
        else if (schedule.SecondaryDayOfMonth.HasValue)
        {
            throw new ArgumentException(
                "A secondary pay day is only valid for semi-monthly schedules.",
                nameof(schedule));
        }
    }

    private static void ValidateMoney(
        decimal amount,
        string parameterName)
    {
        if (amount < 0m)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Money values cannot be negative.");
        }

        if (decimal.Round(
                amount,
                2,
                MidpointRounding.AwayFromZero) !=
            amount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                "Money values must not contain fractions of a cent.");
        }
    }

    private static decimal RoundMoney(
        decimal amount) =>
        decimal.Round(
            amount,
            2,
            MidpointRounding.AwayFromZero);
}

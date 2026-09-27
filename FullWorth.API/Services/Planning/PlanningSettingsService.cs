using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Planning;

public sealed record PlanningPayScheduleSnapshot(
    PayScheduleFrequency Frequency,
    DateOnly AnchorPayDate,
    int? SecondaryDayOfMonth,
    int DefaultPaychecksAhead);

public sealed record PlanningBillFundingPreferenceSnapshot(
    Guid BillStreamId,
    int PaychecksAheadOverride);

public sealed class PlanningSettingsService(
    FullWorthDbContext dbContext,
    IBillStreamReadGateway billStreamGateway,
    TimeProvider timeProvider)
{
    public async Task<PlanningPayScheduleSnapshot?> GetPayScheduleAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        return await dbContext.PlanningPaySchedules
            .AsNoTracking()
            .Where(schedule => schedule.UserId == userId)
            .Select(schedule =>
                new PlanningPayScheduleSnapshot(
                    schedule.Frequency,
                    schedule.AnchorPayDate,
                    schedule.SecondaryDayOfMonth,
                    schedule.DefaultPaychecksAhead))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<PlanningPayScheduleSnapshot> SavePayScheduleAsync(
        Guid userId,
        PayScheduleFrequency frequency,
        DateOnly anchorPayDate,
        int? secondaryDayOfMonth,
        int defaultPaychecksAhead,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ValidateSchedule(
            frequency,
            anchorPayDate,
            secondaryDayOfMonth,
            defaultPaychecksAhead);

        var now = timeProvider.GetUtcNow();

        var schedule =
            await dbContext.PlanningPaySchedules
                .SingleOrDefaultAsync(
                    candidate => candidate.UserId == userId,
                    cancellationToken);

        if (schedule is null)
        {
            schedule =
                new PlanningPayScheduleEntity
                {
                    UserId = userId,
                    Frequency = frequency,
                    AnchorPayDate = anchorPayDate,
                    SecondaryDayOfMonth = secondaryDayOfMonth,
                    DefaultPaychecksAhead = defaultPaychecksAhead,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            dbContext.PlanningPaySchedules.Add(schedule);
        }
        else
        {
            schedule.Frequency = frequency;
            schedule.AnchorPayDate = anchorPayDate;
            schedule.SecondaryDayOfMonth = secondaryDayOfMonth;
            schedule.DefaultPaychecksAhead = defaultPaychecksAhead;
            schedule.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new PlanningPayScheduleSnapshot(
            schedule.Frequency,
            schedule.AnchorPayDate,
            schedule.SecondaryDayOfMonth,
            schedule.DefaultPaychecksAhead);
    }

    public async Task<IReadOnlyList<PlanningBillFundingPreferenceSnapshot>>
        GetBillFundingPreferencesAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);

        return await dbContext.PlanningBillFundingPreferences
            .AsNoTracking()
            .Where(preference =>
                preference.UserId == userId &&
                preference.PaychecksAheadOverride != null)
            .OrderBy(preference => preference.BillStreamId)
            .Select(preference =>
                new PlanningBillFundingPreferenceSnapshot(
                    preference.BillStreamId,
                    preference.PaychecksAheadOverride!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SaveBillFundingPreferenceAsync(
        Guid userId,
        Guid billStreamId,
        int paychecksAheadOverride,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ValidateBillStreamId(billStreamId);

        if (paychecksAheadOverride is < 1 or > 26)
        {
            throw new ArgumentOutOfRangeException(
                nameof(paychecksAheadOverride),
                "Paychecks-ahead override must be between 1 and 26.");
        }

        var ownedBill =
            await billStreamGateway.GetOwnedAsync(
                userId,
                billStreamId,
                cancellationToken);

        if (ownedBill is null)
        {
            return false;
        }

        var now = timeProvider.GetUtcNow();

        var preference =
            await dbContext.PlanningBillFundingPreferences
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.UserId == userId &&
                        candidate.BillStreamId == billStreamId,
                    cancellationToken);

        if (preference is null)
        {
            preference =
                new PlanningBillFundingPreferenceEntity
                {
                    UserId = userId,
                    BillStreamId = billStreamId,
                    PaychecksAheadOverride = paychecksAheadOverride,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            dbContext.PlanningBillFundingPreferences.Add(preference);
        }
        else
        {
            preference.PaychecksAheadOverride = paychecksAheadOverride;
            preference.UpdatedAtUtc = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteBillFundingPreferenceAsync(
        Guid userId,
        Guid billStreamId,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ValidateBillStreamId(billStreamId);

        var ownedBill =
            await billStreamGateway.GetOwnedAsync(
                userId,
                billStreamId,
                cancellationToken);

        if (ownedBill is null)
        {
            return false;
        }

        var preference =
            await dbContext.PlanningBillFundingPreferences
                .SingleOrDefaultAsync(
                    candidate =>
                        candidate.UserId == userId &&
                        candidate.BillStreamId == billStreamId,
                    cancellationToken);

        if (preference is not null)
        {
            dbContext.PlanningBillFundingPreferences.Remove(preference);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static void ValidateSchedule(
        PayScheduleFrequency frequency,
        DateOnly anchorPayDate,
        int? secondaryDayOfMonth,
        int defaultPaychecksAhead)
    {
        if (!Enum.IsDefined(frequency))
        {
            throw new ArgumentOutOfRangeException(nameof(frequency));
        }

        if (defaultPaychecksAhead is < 1 or > 26)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defaultPaychecksAhead),
                "Default paychecks ahead must be between 1 and 26.");
        }

        if (frequency == PayScheduleFrequency.SemiMonthly)
        {
            if (secondaryDayOfMonth is < 1 or > 31)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(secondaryDayOfMonth),
                    "Semi-monthly schedules require a secondary day between 1 and 31.");
            }

            if (secondaryDayOfMonth == anchorPayDate.Day)
            {
                throw new ArgumentException(
                    "Semi-monthly schedule days must be distinct.",
                    nameof(secondaryDayOfMonth));
            }

            return;
        }

        if (secondaryDayOfMonth is not null)
        {
            throw new ArgumentException(
                "Secondary day is only valid for semi-monthly schedules.",
                nameof(secondaryDayOfMonth));
        }
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }

    private static void ValidateBillStreamId(Guid billStreamId)
    {
        if (billStreamId == Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }
    }
}

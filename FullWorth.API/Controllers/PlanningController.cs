using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models.Planning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/planning")]
[Authorize]
public sealed class PlanningController(
    PlanningSettingsService planningSettings,
    PlanningPaydayPlanService paydayPlanService,
    PlanningPaycheckAllocationStore allocationStore,
    IBillStreamReadGateway billStreamGateway,
    PlanningBillChangeWatchService changeWatchService,
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    [HttpGet("pay-schedule")]
    public async Task<ActionResult<PlanningPayScheduleResponse>>
        GetPaySchedule(
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var schedule =
            await planningSettings.GetPayScheduleAsync(
                userId,
                cancellationToken);

        if (schedule is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(schedule));
    }

    [HttpPut("pay-schedule")]
    public async Task<ActionResult<PlanningPayScheduleResponse>>
        PutPaySchedule(
            PlanningPayScheduleRequest request,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (!TryParseFrequency(
                request.Frequency,
                out var frequency))
        {
            return BadRequest(
                new
                {
                    message =
                        "Pay schedule frequency is invalid."
                });
        }

        if (request.DefaultPaychecksAhead is < 1 or > 26)
        {
            return BadRequest(
                new
                {
                    message =
                        "Default paychecks ahead must be between 1 and 26."
                });
        }

        if (frequency == PayScheduleFrequency.SemiMonthly)
        {
            if (request.SecondaryDayOfMonth is < 1 or > 31 ||
                request.SecondaryDayOfMonth == request.AnchorPayDate.Day)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Semi-monthly schedules require two distinct days between 1 and 31."
                    });
            }
        }
        else if (request.SecondaryDayOfMonth is not null)
        {
            return BadRequest(
                new
                {
                    message =
                        "Secondary day is only valid for semi-monthly schedules."
                });
        }

        var schedule =
            await planningSettings.SavePayScheduleAsync(
                userId,
                frequency,
                request.AnchorPayDate,
                request.SecondaryDayOfMonth,
                request.DefaultPaychecksAhead,
                cancellationToken);

        return Ok(ToResponse(schedule));
    }

    [HttpGet("bill-funding-preferences")]
    public async Task<ActionResult<IReadOnlyList<PlanningBillFundingPreferenceResponse>>>
        GetBillFundingPreferences(
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var preferences =
            await planningSettings.GetBillFundingPreferencesAsync(
                userId,
                cancellationToken);

        return Ok(
            preferences
                .Select(
                    preference =>
                        new PlanningBillFundingPreferenceResponse(
                            preference.BillStreamId,
                            preference.PaychecksAheadOverride))
                .ToList());
    }

    [HttpPut("bill-funding-preferences/{billStreamId:guid}")]
    public async Task<ActionResult<PlanningBillFundingPreferenceResponse>>
        PutBillFundingPreference(
            Guid billStreamId,
            PlanningBillFundingPreferenceRequest request,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (billStreamId == Guid.Empty)
        {
            return NotFound();
        }

        if (request.PaychecksAheadOverride is < 1 or > 26)
        {
            return BadRequest(
                new
                {
                    message =
                        "Paychecks-ahead override must be between 1 and 26."
                });
        }

        var saved =
            await planningSettings.SaveBillFundingPreferenceAsync(
                userId,
                billStreamId,
                request.PaychecksAheadOverride,
                cancellationToken);

        if (!saved)
        {
            return NotFound();
        }

        return Ok(
            new PlanningBillFundingPreferenceResponse(
                billStreamId,
                request.PaychecksAheadOverride));
    }

    [HttpDelete("bill-funding-preferences/{billStreamId:guid}")]
    public async Task<IActionResult> DeleteBillFundingPreference(
        Guid billStreamId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (billStreamId == Guid.Empty)
        {
            return NotFound();
        }

        var owned =
            await planningSettings.DeleteBillFundingPreferenceAsync(
                userId,
                billStreamId,
                cancellationToken);

        if (!owned)
        {
            return NotFound();
        }

        return NoContent();
    }


    [HttpGet("upcoming-bill-changes")]
    public async Task<ActionResult<IReadOnlyList<PlanningBillChangeWatchResponse>>>
        GetUpcomingBillChanges(
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var changes =
            await changeWatchService.GetAsync(
                userId,
                cancellationToken);

        return Ok(
            changes
                .Select(
                    change =>
                        new PlanningBillChangeWatchResponse(
                            change.BillStreamId,
                            change.ProviderName,
                            change.ChangeId,
                            change.PreviousStatementId,
                            change.CurrentStatementId,
                            change.BillPeriodEnd,
                            change.BillDueDate,
                            change.CurrencyCode,
                            change.PreviousAmount,
                            change.CurrentAmount,
                            change.AmountDifference,
                            change.AnnualizedImpact,
                            change.Description,
                            change.RecalculationStatus.ToString(),
                            change.AlreadyPlanned,
                            change.RemainingAmountToPlan))
                .ToList());
    }

    [HttpGet("payday-plans/recent")]
    public async Task<ActionResult<IReadOnlyList<PlanningPaydayPlanSummaryResponse>>>
        GetRecentPaydayPlans(
            [FromQuery] int take = 5,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (take is < 1 or > 20)
        {
            return BadRequest(
                new
                {
                    message =
                        "Recent payday plan count must be between 1 and 20."
                });
        }

        var plans =
            await allocationStore
                .GetRecentPaycheckPlanHistoryAsync(
                    userId,
                    take,
                    cancellationToken);

        var billIds =
            plans
                .SelectMany(
                    plan =>
                        plan.Allocations)
                .Select(
                    allocation =>
                        allocation.BillStreamId)
                .Distinct()
                .ToHashSet();

        var billNameById =
            new Dictionary<Guid, string>();

        if (billIds.Count > 0)
        {
            var ownedBills =
                await billStreamGateway.ListOwnedActiveAsync(
                    userId,
                    cancellationToken);

            billNameById =
                ownedBills
                    .Where(
                        bill =>
                            billIds.Contains(
                                bill.BillStreamId))
                    .ToDictionary(
                        bill =>
                            bill.BillStreamId,
                        bill =>
                            bill.ProviderName);
        }

        return Ok(
            plans
                .Select(
                    plan =>
                        new PlanningPaydayPlanSummaryResponse(
                            plan.Run.Id,
                            plan.Run.PaycheckPostedDate,
                            plan.Run.PaycheckAmount,
                            plan.Run.CurrencyCode,
                            plan.Run.RecommendedSetAside,
                            plan.Run.PaycheckRemainingAfterPlan,
                            plan.Run.Shortfall,
                            plan.Run.CreatedAtUtc,
                            plan.Allocations
                                .Select(
                                    allocation =>
                                        new PlanningPaydayPlanHistoryItemResponse(
                                            billNameById.TryGetValue(
                                                allocation.BillStreamId,
                                                out var providerName)
                                                ? providerName
                                                : null,
                                            allocation.BillPeriodEnd,
                                            allocation.BillDueDate,
                                            allocation.PlannedAmount,
                                            allocation.CurrencyCode))
                                .ToList()))
                .ToList());
    }

    [HttpPut("payday-plans/{payrollTransactionId:guid}")]
    public async Task<ActionResult<PlanningPaydayPlanResponse>>
        PutPaydayPlan(
            Guid payrollTransactionId,
            PlanningPaydayPlanRequest request,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (payrollTransactionId == Guid.Empty)
        {
            return NotFound();
        }

        var plan =
            await paydayPlanService.GenerateAsync(
                userId,
                payrollTransactionId,
                request.PostedDate,
                cancellationToken);

        return plan.Status switch
        {
            PlanningPaydayPlanStatus.Ready =>
                Ok(
                    ToResponse(
                        plan)),

            PlanningPaydayPlanStatus.PayrollNotFound =>
                NotFound(),

            PlanningPaydayPlanStatus.PayScheduleRequired =>
                Conflict(
                    new
                    {
                        message =
                            "Configure a pay schedule before generating a payday plan."
                    }),

            PlanningPaydayPlanStatus.PayrollFactUnsupported =>
                UnprocessableEntity(
                    new
                    {
                        message =
                            "This payroll transaction cannot be used for payday planning."
                    }),

            _ =>
                throw new InvalidOperationException(
                    "Planning payday plan status is invalid.")
        };
    }

    private bool TryGetUserId(out Guid userId)
    {
        var userIdText =
            userManager.GetUserId(User);

        return Guid.TryParse(
            userIdText,
            out userId);
    }

    private static bool TryParseFrequency(
        string? value,
        out PayScheduleFrequency frequency)
    {
        frequency = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized =
            value.Trim();

        return Enum.GetNames<PayScheduleFrequency>()
            .Any(
                name =>
                    string.Equals(
                        name,
                        normalized,
                        StringComparison.OrdinalIgnoreCase)) &&
            Enum.TryParse(
                normalized,
                ignoreCase: true,
                out frequency);
    }

    private static PlanningPayScheduleResponse ToResponse(
        PlanningPayScheduleSnapshot schedule)
    {
        return new PlanningPayScheduleResponse(
            schedule.Frequency.ToString(),
            schedule.AnchorPayDate,
            schedule.SecondaryDayOfMonth,
            schedule.DefaultPaychecksAhead);
    }

    private static PlanningPaydayPlanResponse ToResponse(
        PlanningPaydayPlanSnapshot plan)
    {
        return new PlanningPaydayPlanResponse(
            plan.PayrollTransactionId,
            plan.PaycheckPostedDate,
            plan.PaycheckAmount,
            plan.CurrencyCode!,
            plan.IsReplay,
            plan.RecommendedSetAside,
            plan.PaycheckRemainingAfterPlan,
            plan.Shortfall,
            plan.Items
                .Select(
                    item =>
                        new PlanningPaydayPlanItemResponse(
                            item.BillStreamId,
                            item.ProviderName,
                            item.SourceStatementId,
                            item.BillPeriodEnd,
                            item.BillDueDate,
                            item.PlannedAmount,
                            item.CurrencyCode))
                .ToList(),
            plan.SkippedBills
                .Select(
                    item =>
                        new PlanningPaydayPlanSkippedBillResponse(
                            item.BillStreamId,
                            item.ProviderName,
                            item.Reason.ToString()))
                .ToList());
    }
}

public sealed record PlanningPayScheduleRequest(
    string Frequency,
    DateOnly AnchorPayDate,
    int? SecondaryDayOfMonth,
    int DefaultPaychecksAhead);

public sealed record PlanningPayScheduleResponse(
    string Frequency,
    DateOnly AnchorPayDate,
    int? SecondaryDayOfMonth,
    int DefaultPaychecksAhead);

public sealed record PlanningBillFundingPreferenceRequest(
    int PaychecksAheadOverride);

public sealed record PlanningBillFundingPreferenceResponse(
    Guid BillStreamId,
    int PaychecksAheadOverride);


public sealed record PlanningBillChangeWatchResponse(
    Guid BillStreamId,
    string ProviderName,
    Guid ChangeId,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    DateOnly BillPeriodEnd,
    DateOnly? BillDueDate,
    string? CurrencyCode,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    string Description,
    string RecalculationStatus,
    decimal? AlreadyPlanned,
    decimal? RemainingAmountToPlan);

public sealed record PlanningPaydayPlanSummaryResponse(
    Guid Id,
    DateOnly PaycheckPostedDate,
    decimal PaycheckAmount,
    string CurrencyCode,
    decimal RecommendedSetAside,
    decimal PaycheckRemainingAfterPlan,
    decimal Shortfall,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<PlanningPaydayPlanHistoryItemResponse> Items);

public sealed record PlanningPaydayPlanHistoryItemResponse(
    string? ProviderName,
    DateOnly BillPeriodEnd,
    DateOnly BillDueDate,
    decimal PlannedAmount,
    string CurrencyCode);

public sealed record PlanningPaydayPlanRequest(
    DateOnly PostedDate);

public sealed record PlanningPaydayPlanResponse(
    Guid PayrollTransactionId,
    DateOnly PaycheckPostedDate,
    decimal PaycheckAmount,
    string CurrencyCode,
    bool IsReplay,
    decimal RecommendedSetAside,
    decimal PaycheckRemainingAfterPlan,
    decimal Shortfall,
    IReadOnlyList<PlanningPaydayPlanItemResponse> Items,
    IReadOnlyList<PlanningPaydayPlanSkippedBillResponse> SkippedBills);

public sealed record PlanningPaydayPlanItemResponse(
    Guid BillStreamId,
    string? ProviderName,
    Guid SourceStatementId,
    DateOnly BillPeriodEnd,
    DateOnly BillDueDate,
    decimal PlannedAmount,
    string CurrencyCode);

public sealed record PlanningPaydayPlanSkippedBillResponse(
    Guid BillStreamId,
    string ProviderName,
    string Reason);

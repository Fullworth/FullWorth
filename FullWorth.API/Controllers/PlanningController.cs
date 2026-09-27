using FullWorth.API.Data.Entities;
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

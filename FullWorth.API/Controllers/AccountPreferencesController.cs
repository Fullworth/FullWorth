using FullWorth.API.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/account/preferences")]
[Authorize]
public sealed class AccountPreferencesController(
    UserManager<ApplicationUser> userManager)
    : ControllerBase
{
    private static readonly ExperienceFocus[]
        SupportedFocusAreas =
        [
            ExperienceFocus.BillChanges,
            ExperienceFocus.Spending,
            ExperienceFocus.RecurringCosts,
            ExperienceFocus.AccountOverview,
            ExperienceFocus.Statements
        ];

    [HttpGet]
    public async Task<ActionResult<AccountPreferencesResponse>> Get(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        return Ok(ToResponse(user));
    }

    [HttpPut]
    public async Task<ActionResult<AccountPreferencesResponse>> Update(
        UpdateAccountPreferencesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        if (!TryParseNamedEnum(
                request.TimestampDisplayMode,
                out TimestampDisplayMode displayMode))
        {
            return ValidationProblem(
                "Timestamp display mode is invalid.");
        }

        user.TimestampDisplayMode = displayMode;

        var updateResult = await userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "FullWorth could not save your timestamp preference.");
        }

        return Ok(ToResponse(user));
    }

    [HttpPut("experience")]
    public async Task<ActionResult<AccountPreferencesResponse>>
        UpdateExperience(
            UpdateExperiencePreferencesRequest request,
            CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();

        if (user is null)
        {
            return NotFound();
        }

        var preferredUiLanguage =
            NormalizeUiLanguage(
                request.PreferredUiLanguage);

        if (preferredUiLanguage is null)
        {
            return ValidationProblem(
                "Preferred UI language is invalid.");
        }

        if (!TryParseNamedEnum(
                request.ThemePreference,
                out UiThemePreference themePreference))
        {
            return ValidationProblem(
                "Theme preference is invalid.");
        }

        if (!TryParseNamedEnum(
                request.TextSizePreference,
                out UiTextSizePreference textSizePreference))
        {
            return ValidationProblem(
                "Text size preference is invalid.");
        }

        if (!TryParseFocusAreas(
                request.ExperienceFocus,
                out var experienceFocus))
        {
            return ValidationProblem(
                "Experience focus is invalid.");
        }

        user.PreferredUiLanguage =
            preferredUiLanguage;

        user.ThemePreference =
            themePreference;

        user.TextSizePreference =
            textSizePreference;

        user.HighContrastEnabled =
            request.HighContrastEnabled;

        user.ReduceMotionEnabled =
            request.ReduceMotionEnabled;

        user.ExperienceFocus =
            experienceFocus;

        user.ExperienceSetupCompletedAtUtc ??=
            DateTimeOffset.UtcNow;

        var updateResult =
            await userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            return Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "FullWorth could not save your experience preferences.");
        }

        return Ok(ToResponse(user));
    }

    private async Task<ApplicationUser?> GetCurrentUserAsync()
    {
        var userIdText = userManager.GetUserId(User);

        if (!Guid.TryParse(userIdText, out var userId))
        {
            return null;
        }

        return await userManager.FindByIdAsync(
            userId.ToString());
    }

    private static AccountPreferencesResponse ToResponse(
        ApplicationUser user)
    {
        var focusAreas =
            SupportedFocusAreas
                .Where(
                    focus =>
                        user.ExperienceFocus.HasFlag(
                            focus))
                .Select(
                    focus =>
                        focus.ToString())
                .ToArray();

        return new AccountPreferencesResponse(
            user.TimestampDisplayMode.ToString(),
            user.ExperienceSetupCompletedAtUtc is not null,
            user.PreferredUiLanguage,
            user.ThemePreference.ToString(),
            user.TextSizePreference.ToString(),
            user.HighContrastEnabled,
            user.ReduceMotionEnabled,
            focusAreas);
    }

    private static string? NormalizeUiLanguage(
        string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "en" or "en-us" =>
                "en-US",

            "es" or "es-es" or "es-us" =>
                "es",

            _ =>
                null
        };
    }

    private static bool TryParseFocusAreas(
        string[]? values,
        out ExperienceFocus result)
    {
        result =
            ExperienceFocus.None;

        if (values is null ||
            values.Length == 0 ||
            values.Length >
                SupportedFocusAreas.Length)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (!TryParseNamedEnum(
                    value,
                    out ExperienceFocus focus) ||
                !SupportedFocusAreas.Contains(
                    focus))
            {
                return false;
            }

            result |=
                focus;
        }

        return result !=
            ExperienceFocus.None;
    }

    private static bool TryParseNamedEnum<TEnum>(
        string? value,
        out TEnum result)
        where TEnum : struct, Enum
    {
        result =
            default;

        if (string.IsNullOrWhiteSpace(
                value) ||
            int.TryParse(
                value,
                out _))
        {
            return false;
        }

        return Enum.TryParse(
                   value.Trim(),
                   ignoreCase: true,
                   out result) &&
               Enum.IsDefined(
                   result);
    }
}

public sealed record AccountPreferencesResponse(
    string TimestampDisplayMode,
    bool ExperienceSetupComplete,
    string PreferredUiLanguage,
    string ThemePreference,
    string TextSizePreference,
    bool HighContrastEnabled,
    bool ReduceMotionEnabled,
    string[] ExperienceFocus);

public sealed record UpdateAccountPreferencesRequest(
    string TimestampDisplayMode);

public sealed record UpdateExperiencePreferencesRequest(
    string PreferredUiLanguage,
    string ThemePreference,
    string TextSizePreference,
    bool HighContrastEnabled,
    bool ReduceMotionEnabled,
    string[] ExperienceFocus);

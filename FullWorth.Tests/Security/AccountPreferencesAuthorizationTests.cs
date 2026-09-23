using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class AccountPreferencesAuthorizationTests
{
    [Fact]
    public async Task Get_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.GetAsync("/api/account/preferences");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new { timestampDisplayMode = "Utc" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ChangesOnlyAuthenticatedUsersPreference()
    {
        await using var factory = new FullWorthApiFactory();
        using var firstClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var first = await TestUserAuthentication.RegisterAndLoginAsync(firstClient);
        var second = await TestUserAuthentication.RegisterAndLoginAsync(secondClient);
        var firstId = await TestUserAuthentication.GetUserIdAsync(factory, first.Email);
        var secondId = await TestUserAuthentication.GetUserIdAsync(factory, second.Email);

        firstClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", first.AccessToken);

        using var response = await firstClient.PutAsJsonAsync(
            "/api/account/preferences",
            new { timestampDisplayMode = "Utc" });

        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var firstMode = await dbContext.Users
            .Where(user => user.Id == firstId)
            .Select(user => user.TimestampDisplayMode)
            .SingleAsync();
        var secondMode = await dbContext.Users
            .Where(user => user.Id == secondId)
            .Select(user => user.TimestampDisplayMode)
            .SingleAsync();

        Assert.Equal(TimestampDisplayMode.Utc, firstMode);
        Assert.Equal(TimestampDisplayMode.Local12Hour, secondMode);
    }

    [Fact]
    public async Task ExperiencePut_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        using var response = await client.PutAsJsonAsync(
            "/api/account/preferences/experience",
            new
            {
                preferredUiLanguage = "en-US",
                themePreference = "System",
                textSizePreference = "Large",
                highContrastEnabled = true,
                reduceMotionEnabled = false,
                experienceFocus = new[] { "BillChanges", "AccountOverview" }
            });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ExperiencePut_ChangesOnlyAuthenticatedUsersSetup()
    {
        await using var factory = new FullWorthApiFactory();
        using var firstClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var first = await TestUserAuthentication.RegisterAndLoginAsync(firstClient);
        var second = await TestUserAuthentication.RegisterAndLoginAsync(secondClient);
        var firstId = await TestUserAuthentication.GetUserIdAsync(factory, first.Email);
        var secondId = await TestUserAuthentication.GetUserIdAsync(factory, second.Email);

        firstClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", first.AccessToken);

        using var response = await firstClient.PutAsJsonAsync(
            "/api/account/preferences/experience",
            new
            {
                preferredUiLanguage = "es",
                themePreference = "Dark",
                textSizePreference = "ExtraLarge",
                highContrastEnabled = true,
                reduceMotionEnabled = true,
                experienceFocus = new[] { "BillChanges", "RecurringCosts" }
            });

        response.EnsureSuccessStatusCode();

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();

        var firstPreferences = await dbContext.Users
            .Where(user => user.Id == firstId)
            .Select(user => new
            {
                user.ExperienceSetupCompletedAtUtc,
                user.PreferredUiLanguage,
                user.ThemePreference,
                user.TextSizePreference,
                user.HighContrastEnabled,
                user.ReduceMotionEnabled,
                user.ExperienceFocus
            })
            .SingleAsync();

        var secondPreferences = await dbContext.Users
            .Where(user => user.Id == secondId)
            .Select(user => new
            {
                user.ExperienceSetupCompletedAtUtc,
                user.PreferredUiLanguage,
                user.ThemePreference,
                user.TextSizePreference,
                user.HighContrastEnabled,
                user.ReduceMotionEnabled,
                user.ExperienceFocus
            })
            .SingleAsync();

        Assert.NotNull(firstPreferences.ExperienceSetupCompletedAtUtc);
        Assert.Equal("es", firstPreferences.PreferredUiLanguage);
        Assert.Equal(UiThemePreference.Dark, firstPreferences.ThemePreference);
        Assert.Equal(UiTextSizePreference.ExtraLarge, firstPreferences.TextSizePreference);
        Assert.True(firstPreferences.HighContrastEnabled);
        Assert.True(firstPreferences.ReduceMotionEnabled);
        Assert.Equal(
            ExperienceFocus.BillChanges | ExperienceFocus.RecurringCosts,
            firstPreferences.ExperienceFocus);

        Assert.Null(secondPreferences.ExperienceSetupCompletedAtUtc);
        Assert.Equal("en-US", secondPreferences.PreferredUiLanguage);
        Assert.Equal(UiThemePreference.System, secondPreferences.ThemePreference);
        Assert.Equal(UiTextSizePreference.Standard, secondPreferences.TextSizePreference);
        Assert.False(secondPreferences.HighContrastEnabled);
        Assert.False(secondPreferences.ReduceMotionEnabled);
        Assert.Equal(
            ExperienceFocus.BillChanges | ExperienceFocus.AccountOverview,
            secondPreferences.ExperienceFocus);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-mode")]
    [InlineData("2")]
    public async Task Put_InvalidPreference_IsRejected(string mode)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);

        using var response = await client.PutAsJsonAsync(
            "/api/account/preferences",
            new { timestampDisplayMode = mode });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

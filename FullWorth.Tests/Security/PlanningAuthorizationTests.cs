using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class PlanningAuthorizationTests
{
    [Fact]
    public async Task PlanningEndpoints_RequireAuthentication()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var schedule =
            await client.GetAsync(
                "/api/planning/pay-schedule");

        using var preferences =
            await client.GetAsync(
                "/api/planning/bill-funding-preferences");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            schedule.StatusCode);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            preferences.StatusCode);
    }

    [Fact]
    public async Task PaySchedule_IsScopedToAuthenticatedUser()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var firstClient =
            factory.CreateHttpsClient();

        using var secondClient =
            factory.CreateHttpsClient();

        var first =
            await TestUserAuthentication.RegisterAndLoginAsync(
                firstClient);

        var second =
            await TestUserAuthentication.RegisterAndLoginAsync(
                secondClient);

        TestUserAuthentication.Authorize(
            firstClient,
            first);

        using var save =
            await firstClient.PutAsJsonAsync(
                "/api/planning/pay-schedule",
                new
                {
                    frequency = "Biweekly",
                    anchorPayDate =
                        new DateOnly(
                            2026,
                            9,
                            18),
                    secondaryDayOfMonth =
                        (int?)null,
                    defaultPaychecksAhead = 3
                });

        save.EnsureSuccessStatusCode();

        TestUserAuthentication.Authorize(
            secondClient,
            second);

        using var secondRead =
            await secondClient.GetAsync(
                "/api/planning/pay-schedule");

        Assert.Equal(
            HttpStatusCode.NotFound,
            secondRead.StatusCode);

        TestUserAuthentication.Authorize(
            firstClient,
            first);

        using var firstRead =
            await firstClient.GetAsync(
                "/api/planning/pay-schedule");

        firstRead.EnsureSuccessStatusCode();

        var result =
            await firstRead.Content
                .ReadFromJsonAsync<PaySchedulePayload>();

        Assert.NotNull(result);
        Assert.Equal("Biweekly", result.Frequency);
        Assert.Equal(3, result.DefaultPaychecksAhead);
    }

    [Fact]
    public async Task BillPreference_CrossUserIdManipulation_ReturnsNotFoundWithoutMutation()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var ownerClient =
            factory.CreateHttpsClient();

        using var attackerClient =
            factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterAndLoginAsync(
                ownerClient);

        var attacker =
            await TestUserAuthentication.RegisterAndLoginAsync(
                attackerClient);

        var ownerUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                owner.Email);

        var attackerUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                attacker.Email);

        var ownerBillStreamId =
            await SeedBillStreamAsync(
                factory,
                ownerUserId);

        TestUserAuthentication.Authorize(
            attackerClient,
            attacker);

        using var attackPut =
            await attackerClient.PutAsJsonAsync(
                $"/api/planning/bill-funding-preferences/{ownerBillStreamId}",
                new
                {
                    paychecksAheadOverride = 2
                });

        Assert.Equal(
            HttpStatusCode.NotFound,
            attackPut.StatusCode);

        await AssertNoPreferenceAsync(
            factory,
            attackerUserId,
            ownerBillStreamId);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var ownerPut =
            await ownerClient.PutAsJsonAsync(
                $"/api/planning/bill-funding-preferences/{ownerBillStreamId}",
                new
                {
                    paychecksAheadOverride = 4
                });

        ownerPut.EnsureSuccessStatusCode();

        TestUserAuthentication.Authorize(
            attackerClient,
            attacker);

        using var attackDelete =
            await attackerClient.DeleteAsync(
                $"/api/planning/bill-funding-preferences/{ownerBillStreamId}");

        Assert.Equal(
            HttpStatusCode.NotFound,
            attackDelete.StatusCode);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        var ownerPreference =
            await dbContext.PlanningBillFundingPreferences
                .SingleAsync(
                    preference =>
                        preference.UserId == ownerUserId &&
                        preference.BillStreamId == ownerBillStreamId);

        Assert.Equal(
            4,
            ownerPreference.PaychecksAheadOverride);
    }

    [Theory]
    [InlineData("2")]
    [InlineData("NotReal")]
    public async Task PaySchedule_RejectsInvalidFrequency(
        string frequency)
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var user =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            user);

        using var response =
            await client.PutAsJsonAsync(
                "/api/planning/pay-schedule",
                new
                {
                    frequency,
                    anchorPayDate =
                        new DateOnly(
                            2026,
                            9,
                            18),
                    secondaryDayOfMonth =
                        (int?)null,
                    defaultPaychecksAhead = 3
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    private static async Task<Guid> SeedBillStreamAsync(
        FullWorthApiFactory factory,
        Guid userId)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        var stream =
            new BillStreamEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ProviderName = "Planning ownership fixture",
                Category = BillCategory.Utility,
                Source = BillStreamSource.Manual,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

        dbContext.BillStreams.Add(stream);
        await dbContext.SaveChangesAsync();
        return stream.Id;
    }

    private static async Task AssertNoPreferenceAsync(
        FullWorthApiFactory factory,
        Guid userId,
        Guid billStreamId)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        Assert.False(
            await dbContext.PlanningBillFundingPreferences
                .AnyAsync(
                    preference =>
                        preference.UserId == userId &&
                        preference.BillStreamId == billStreamId));
    }

    private sealed class PaySchedulePayload
    {
        public string Frequency { get; set; } =
            string.Empty;

        public DateOnly AnchorPayDate { get; set; }

        public int? SecondaryDayOfMonth { get; set; }

        public int DefaultPaychecksAhead { get; set; }
    }
}

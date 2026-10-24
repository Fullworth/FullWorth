using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Controllers;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class PaydayReplayHistoricalNamesAuthorizationTests
{
    [Fact]
    public async Task InactiveOwnedBill_NameAgreesAcrossReplayAndHistory_WithoutReactivation()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        using var otherClient = factory.CreateHttpsClient();
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var other = await TestUserAuthentication.RegisterAndLoginAsync(otherClient);
        var ownerId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        var payroll = Guid.NewGuid();
        var posted = new DateOnly(2026, 9, 25);
        var billId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            db.BillStreams.Add(new BillStreamEntity
            {
                Id = billId, UserId = ownerId, ProviderName = "Synthetic Historical Utilities",
                Category = BillCategory.Utility, Source = BillStreamSource.Manual, IsActive = false
            });
            await db.SaveChangesAsync();
            var store = new PlanningPaycheckAllocationStore(db, TimeProvider.System);
            await store.SaveCompletePaycheckPlanAsync(ownerId, payroll,
                new PlanningPaycheckPlanRunDraft(posted, 1000m, "USD", 125m, 875m, 0m)
                { Coverage = new PlanningPaycheckPlanCoverage(1, 0, 0, 0, 0, 0) },
                [new PlanningPaycheckAllocationDraft(billId, Guid.NewGuid(),
                    new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 23), 125m, "USD")]);
        }
        TestUserAuthentication.Authorize(client, owner);
        using var response = await client.PutAsJsonAsync(
            $"/api/planning/payday-plans/{payroll}", new { postedDate = posted });
        response.EnsureSuccessStatusCode();
        var replay = await response.Content.ReadFromJsonAsync<PlanningPaydayPlanResponse>();
        Assert.NotNull(replay);
        Assert.True(replay.IsReplay);
        Assert.Equal("Synthetic Historical Utilities", Assert.Single(replay.Items).ProviderName);
        Assert.Equal(125m, replay.RecommendedSetAside);
        var history = await client.GetFromJsonAsync<List<PlanningPaydayPlanSummaryResponse>>(
            "/api/planning/payday-plans/recent");
        Assert.NotNull(history);
        Assert.Equal(Assert.Single(replay.Items).ProviderName,
            Assert.Single(Assert.Single(history).Items).ProviderName);

        TestUserAuthentication.Authorize(otherClient, other);
        using var forbidden = await otherClient.PutAsJsonAsync(
            $"/api/planning/payday-plans/{payroll}", new { postedDate = posted });
        Assert.Equal(HttpStatusCode.NotFound, forbidden.StatusCode);
        var otherHistory = await otherClient.GetFromJsonAsync<List<PlanningPaydayPlanSummaryResponse>>(
            "/api/planning/payday-plans/recent");
        Assert.NotNull(otherHistory);
        Assert.Empty(otherHistory);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verifyDb = verificationScope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        Assert.False((await verifyDb.BillStreams.SingleAsync(item => item.Id == billId)).IsActive);
        Assert.Single(await verifyDb.PlanningPaycheckPlanRuns.Where(run => run.UserId == ownerId).ToListAsync());
    }
}

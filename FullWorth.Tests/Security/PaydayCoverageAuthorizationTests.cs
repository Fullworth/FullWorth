using System.Net.Http.Json;
using FullWorth.API.Controllers;
using FullWorth.API.Data;
using FullWorth.API.Services.Planning;
using FullWorth.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class PaydayCoverageAuthorizationTests
{
    [Fact]
    public async Task RecentAndReplay_ReturnSavedCoverageOnlyToOwner_AndPreserveLegacyUnknown()
    {
        await using var factory = new FullWorthApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var otherClient = factory.CreateHttpsClient();
        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var other = await TestUserAuthentication.RegisterAndLoginAsync(otherClient);
        var userId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        var payroll = Guid.NewGuid();
        var legacyPayroll = Guid.NewGuid();
        var posted = new DateOnly(2026, 9, 25);
        var coverage = new PlanningPaycheckPlanCoverage(0, 1, 2, 3, 4, 5);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            var store = new PlanningPaycheckAllocationStore(db, TimeProvider.System);
            var draft = new PlanningPaycheckPlanRunDraft(posted, 1000m, "USD", 0m, 1000m, 0m);
            await store.SaveCompletePaycheckPlanAsync(userId, payroll, draft with { Coverage = coverage }, []);
            await store.SaveCompletePaycheckPlanAsync(userId, legacyPayroll, draft, []);
        }

        TestUserAuthentication.Authorize(ownerClient, owner);
        using var replayResponse = await ownerClient.PutAsJsonAsync(
            $"/api/planning/payday-plans/{payroll}", new { postedDate = posted });
        replayResponse.EnsureSuccessStatusCode();
        var replay = await replayResponse.Content.ReadFromJsonAsync<PlanningPaydayPlanResponse>();
        Assert.NotNull(replay);
        Assert.True(replay.IsReplay);
        var savedCoverage = Assert.IsType<PlanningPaycheckPlanCoverage>(replay.Coverage);
        Assert.Equal(coverage, savedCoverage);
        Assert.Equal(15, savedCoverage.SkippedBillCount);
        Assert.Empty(replay.Items);
        Assert.Equal(0m, replay.RecommendedSetAside);

        var history = await ownerClient.GetFromJsonAsync<List<PlanningPaydayPlanSummaryResponse>>(
            "/api/planning/payday-plans/recent");
        Assert.NotNull(history);
        Assert.Equal(2, history.Count);
        Assert.Single(history, plan => plan.Coverage == coverage);
        Assert.Single(history, plan => plan.Coverage is null);
        TestUserAuthentication.Authorize(otherClient, other);
        var otherHistory = await otherClient.GetFromJsonAsync<List<PlanningPaydayPlanSummaryResponse>>(
            "/api/planning/payday-plans/recent");
        Assert.NotNull(otherHistory);
        Assert.Empty(otherHistory);
        using var forbiddenReplay = await otherClient.PutAsJsonAsync(
            $"/api/planning/payday-plans/{payroll}", new { postedDate = posted });
        Assert.Equal(System.Net.HttpStatusCode.NotFound, forbiddenReplay.StatusCode);
    }
}

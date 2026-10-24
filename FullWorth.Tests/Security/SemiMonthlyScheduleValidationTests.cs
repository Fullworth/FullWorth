using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models.Planning;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class SemiMonthlyScheduleValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(1)]
    public async Task InvalidSecondDay_ReturnsBadRequest_AndPreservesExistingOwnedAndForeignSchedules(int? day)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var first = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var second = await TestUserAuthentication.RegisterAndLoginAsync(client);
        async Task SaveValid(TestUserSession session)
        {
            TestUserAuthentication.Authorize(client, session);
            using var save = await client.PutAsJsonAsync("/api/planning/pay-schedule",
                new { frequency = "Monthly", anchorPayDate = "2026-10-01", defaultPaychecksAhead = 2 });
            save.EnsureSuccessStatusCode();
        }
        await SaveValid(first); await SaveValid(second);
        TestUserAuthentication.Authorize(client, first);
        using var rejected = await client.PutAsJsonAsync("/api/planning/pay-schedule",
            new { frequency = "SemiMonthly", anchorPayDate = "2026-10-01", secondaryDayOfMonth = day, defaultPaychecksAhead = 5 });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        Assert.All(await db.PlanningPaySchedules.ToListAsync(), row =>
        {
            Assert.Equal(PayScheduleFrequency.Monthly, row.Frequency);
            Assert.Equal(2, row.DefaultPaychecksAhead);
            Assert.Null(row.SecondaryDayOfMonth);
        });
        Assert.Equal(2, await db.PlanningPaySchedules.CountAsync());
    }

    [Fact]
    public async Task DirectService_RejectsMissingDayBeforeTracking_AndValidBoundaryDaysRoundTrip()
    {
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var service = new PlanningSettingsService(db, new BillStreamReadGateway(db), TimeProvider.System);
        var owner = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SavePayScheduleAsync(owner, PayScheduleFrequency.SemiMonthly, new DateOnly(2026, 10, 15), null, 2));
        Assert.Empty(db.ChangeTracker.Entries());
        foreach (var day in new[] { 1, 31 })
        {
            var result = await service.SavePayScheduleAsync(owner, PayScheduleFrequency.SemiMonthly,
                new DateOnly(2026, 10, 15), day, 2);
            Assert.Equal(day, result.SecondaryDayOfMonth);
            Assert.Equal(day, (await service.GetPayScheduleAsync(owner))!.SecondaryDayOfMonth);
        }
        Assert.Equal(1, await db.PlanningPaySchedules.CountAsync());
    }
}

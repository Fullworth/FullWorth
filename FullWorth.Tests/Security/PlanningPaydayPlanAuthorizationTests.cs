using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class PlanningPaydayPlanAuthorizationTests
{
    [Fact]
    public async Task PaydayPlan_RequiresAuthentication()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PutAsJsonAsync(
                $"/api/planning/payday-plans/{Guid.NewGuid()}",
                new
                {
                    postedDate =
                        new DateOnly(
                            2026,
                            9,
                            25)
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task PaydayPlan_CrossUserPayrollId_ReturnsNotFoundWithoutAllocation()
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

        var payrollTransactionId =
            Guid.NewGuid();

        await SeedPayrollAsync(
            factory,
            ownerUserId,
            payrollTransactionId,
            new DateOnly(
                2026,
                9,
                25));

        TestUserAuthentication.Authorize(
            attackerClient,
            attacker);

        using var response =
            await attackerClient.PutAsJsonAsync(
                $"/api/planning/payday-plans/{payrollTransactionId}",
                new
                {
                    postedDate =
                        new DateOnly(
                            2026,
                            9,
                            25)
                });

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        Assert.False(
            await dbContext.PlanningPaycheckAllocations
                .AnyAsync(
                    allocation =>
                        allocation.PayrollTransactionId ==
                        payrollTransactionId));
    }

    [Fact]
    public async Task PaydayPlan_PersistsRecommendation_AndReplayIsStable()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var user =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var userId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                user.Email);

        var billStreamId =
            Guid.NewGuid();

        var payrollTransactionId =
            Guid.NewGuid();

        var postedDate =
            new DateOnly(
                2026,
                9,
                25);

        await SeedPlanFactsAsync(
            factory,
            userId,
            billStreamId,
            payrollTransactionId,
            postedDate);

        TestUserAuthentication.Authorize(
            client,
            user);

        using var schedule =
            await client.PutAsJsonAsync(
                "/api/planning/pay-schedule",
                new
                {
                    frequency =
                        "Biweekly",
                    anchorPayDate =
                        postedDate,
                    secondaryDayOfMonth =
                        (int?)null,
                    defaultPaychecksAhead =
                        2
                });

        schedule.EnsureSuccessStatusCode();

        using var firstResponse =
            await client.PutAsJsonAsync(
                $"/api/planning/payday-plans/{payrollTransactionId}",
                new
                {
                    postedDate
                });

        firstResponse.EnsureSuccessStatusCode();

        var first =
            await firstResponse.Content
                .ReadFromJsonAsync<PaydayPlanPayload>();

        Assert.NotNull(
            first);

        Assert.False(
            first.IsReplay);

        Assert.Equal(
            300m,
            first.RecommendedSetAside);

        Assert.Equal(
            700m,
            first.PaycheckRemainingAfterPlan);

        Assert.Equal(
            0m,
            first.Shortfall);

        Assert.Single(
            first.Items);

        using var replayResponse =
            await client.PutAsJsonAsync(
                $"/api/planning/payday-plans/{payrollTransactionId}",
                new
                {
                    postedDate
                });

        replayResponse.EnsureSuccessStatusCode();

        var replay =
            await replayResponse.Content
                .ReadFromJsonAsync<PaydayPlanPayload>();

        Assert.NotNull(
            replay);

        Assert.True(
            replay.IsReplay);

        Assert.Equal(
            first.RecommendedSetAside,
            replay.RecommendedSetAside);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        Assert.Equal(
            1,
            await dbContext.PlanningPaycheckAllocations
                .CountAsync(
                    allocation =>
                        allocation.UserId ==
                            userId &&
                        allocation.PayrollTransactionId ==
                            payrollTransactionId));
    }

    private static async Task SeedPlanFactsAsync(
        FullWorthApiFactory factory,
        Guid userId,
        Guid billStreamId,
        Guid payrollTransactionId,
        DateOnly postedDate)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        var now =
            DateTimeOffset.UtcNow;

        dbContext.BillStreams.Add(
            new BillStreamEntity
            {
                Id = billStreamId,
                UserId = userId,
                ProviderName =
                    "Planning payday fixture",
                Category =
                    BillCategory.Internet,
                Source =
                    BillStreamSource.Manual,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        dbContext.BillStatements.Add(
            new BillStatementEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BillStreamId = billStreamId,
                PeriodStart =
                    new DateOnly(
                        2026,
                        9,
                        15),
                PeriodEnd =
                    new DateOnly(
                        2026,
                        10,
                        15),
                StatementDate =
                    new DateOnly(
                        2026,
                        10,
                        15),
                DueDate =
                    new DateOnly(
                        2026,
                        10,
                        23),
                TotalAmount =
                    600m,
                CurrencyCode =
                    "USD",
                RetrievedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });

        dbContext.BankTransactions.Add(
            CreatePayroll(
                userId,
                payrollTransactionId,
                postedDate));

        await dbContext.SaveChangesAsync();
    }

    private static async Task SeedPayrollAsync(
        FullWorthApiFactory factory,
        Guid userId,
        Guid payrollTransactionId,
        DateOnly postedDate)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        dbContext.BankTransactions.Add(
            CreatePayroll(
                userId,
                payrollTransactionId,
                postedDate));

        await dbContext.SaveChangesAsync();
    }

    private static BankTransactionEntity CreatePayroll(
        Guid userId,
        Guid payrollTransactionId,
        DateOnly postedDate)
    {
        var now =
            DateTimeOffset.UtcNow;

        return new BankTransactionEntity
        {
            Id = payrollTransactionId,
            UserId = userId,
            BankAccountId = Guid.NewGuid(),
            PlaidTransactionId =
                Guid.NewGuid().ToString("N"),
            Name =
                "Payroll fixture",
            MerchantName =
                "Payroll fixture",
            Amount =
                -1000m,
            IsoCurrencyCode =
                "USD",
            PostedDate =
                postedDate,
            AuthorizedDate =
                postedDate,
            IsPending =
                false,
            IsRemoved =
                false,
            CategoryPrimary =
                "INCOME",
            CategoryDetailed =
                "INCOME_WAGES",
            CreatedAtUtc =
                now,
            UpdatedAtUtc =
                now
        };
    }

    private sealed class PaydayPlanPayload
    {
        public bool IsReplay { get; set; }

        public decimal RecommendedSetAside { get; set; }

        public decimal PaycheckRemainingAfterPlan { get; set; }

        public decimal Shortfall { get; set; }

        public List<PaydayPlanItemPayload> Items { get; set; } =
            [];
    }

    private sealed class PaydayPlanItemPayload
    {
        public Guid BillStreamId { get; set; }

        public decimal PlannedAmount { get; set; }
    }
}

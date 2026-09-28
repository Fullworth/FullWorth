using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using FullWorth.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class PlanningBillChangeWatchAuthorizationTests
{
    [Fact]
    public async Task UpcomingBillChanges_RequiresAuthentication()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/api/planning/upcoming-bill-changes");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task UpcomingBillChanges_ReturnsOnlyAuthenticatedUsersEvidence()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var ownerClient =
            factory.CreateHttpsClient();

        using var otherClient =
            factory.CreateHttpsClient();

        var owner =
            await TestUserAuthentication.RegisterAndLoginAsync(
                ownerClient);

        var other =
            await TestUserAuthentication.RegisterAndLoginAsync(
                otherClient);

        var ownerUserId =
            await TestUserAuthentication.GetUserIdAsync(
                factory,
                owner.Email);

        await SeedChangeWatchAsync(
            factory,
            ownerUserId);

        TestUserAuthentication.Authorize(
            ownerClient,
            owner);

        using var ownerResponse =
            await ownerClient.GetAsync(
                "/api/planning/upcoming-bill-changes");

        ownerResponse.EnsureSuccessStatusCode();

        var ownerItems =
            await ownerResponse.Content
                .ReadFromJsonAsync<List<ChangeWatchPayload>>();

        var ownerItem =
            Assert.Single(
                Assert.IsType<List<ChangeWatchPayload>>(
                    ownerItems));

        Assert.Equal(
            "Internet",
            ownerItem.ProviderName);

        Assert.Equal(
            100m,
            ownerItem.AmountDifference);

        Assert.Equal(
            1200m,
            ownerItem.AnnualizedImpact);

        Assert.Equal(
            200m,
            ownerItem.AlreadyPlanned);

        Assert.Equal(
            400m,
            ownerItem.RemainingAmountToPlan);

        Assert.Equal(
            "Ready",
            ownerItem.RecalculationStatus);

        TestUserAuthentication.Authorize(
            otherClient,
            other);

        using var otherResponse =
            await otherClient.GetAsync(
                "/api/planning/upcoming-bill-changes");

        otherResponse.EnsureSuccessStatusCode();

        var otherItems =
            await otherResponse.Content
                .ReadFromJsonAsync<List<ChangeWatchPayload>>();

        Assert.Empty(
            Assert.IsType<List<ChangeWatchPayload>>(
                otherItems));
    }

    private static async Task SeedChangeWatchAsync(
        FullWorthApiFactory factory,
        Guid userId)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FullWorthDbContext>();

        var now =
            DateTimeOffset.UtcNow;

        var billStreamId =
            Guid.NewGuid();

        var previousStatementId =
            Guid.NewGuid();

        var currentStatementId =
            Guid.NewGuid();

        var periodEnd =
            new DateOnly(
                2026,
                10,
                31);

        dbContext.BillStreams.Add(
            new BillStreamEntity
            {
                Id =
                    billStreamId,
                UserId =
                    userId,
                ProviderName =
                    "Internet",
                Category =
                    BillCategory.Internet,
                Source =
                    BillStreamSource.Manual,
                IsActive =
                    true,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            });

        dbContext.BillStatements.AddRange(
            new BillStatementEntity
            {
                Id =
                    previousStatementId,
                UserId =
                    userId,
                BillStreamId =
                    billStreamId,
                PeriodStart =
                    new DateOnly(
                        2026,
                        9,
                        1),
                PeriodEnd =
                    new DateOnly(
                        2026,
                        9,
                        30),
                StatementDate =
                    new DateOnly(
                        2026,
                        9,
                        30),
                DueDate =
                    new DateOnly(
                        2026,
                        10,
                        15),
                TotalAmount =
                    500m,
                CurrencyCode =
                    "USD",
                RetrievedAtUtc =
                    now,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            },
            new BillStatementEntity
            {
                Id =
                    currentStatementId,
                UserId =
                    userId,
                BillStreamId =
                    billStreamId,
                PeriodStart =
                    new DateOnly(
                        2026,
                        10,
                        1),
                PeriodEnd =
                    periodEnd,
                StatementDate =
                    periodEnd,
                DueDate =
                    new DateOnly(
                        2026,
                        11,
                        15),
                TotalAmount =
                    600m,
                CurrencyCode =
                    "USD",
                RetrievedAtUtc =
                    now,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            });

        dbContext.BillChanges.Add(
            new BillChangeEntity
            {
                Id =
                    Guid.NewGuid(),
                UserId =
                    userId,
                BillStreamId =
                    billStreamId,
                PreviousStatementId =
                    previousStatementId,
                CurrentStatementId =
                    currentStatementId,
                ChangeType =
                    BillChangeType.TotalIncrease,
                Confidence =
                    BillChangeConfidence.Confirmed,
                Description =
                    "Provider statements confirm the monthly total increased by $100.00.",
                PreviousAmount =
                    500m,
                CurrentAmount =
                    600m,
                AmountDifference =
                    100m,
                AnnualizedImpact =
                    1200m,
                IsAcknowledged =
                    false,
                DetectedAtUtc =
                    now,
                CreatedAtUtc =
                    now,
                UpdatedAtUtc =
                    now
            });

        dbContext.PlanningPaycheckAllocations.Add(
            new PlanningPaycheckAllocationEntity
            {
                Id =
                    Guid.NewGuid(),
                UserId =
                    userId,
                PayrollTransactionId =
                    Guid.NewGuid(),
                BillStreamId =
                    billStreamId,
                SourceStatementId =
                    currentStatementId,
                PaycheckPostedDate =
                    new DateOnly(
                        2026,
                        10,
                        2),
                BillPeriodEnd =
                    periodEnd,
                BillDueDate =
                    new DateOnly(
                        2026,
                        11,
                        15),
                PlannedAmount =
                    200m,
                CurrencyCode =
                    "USD",
                CreatedAtUtc =
                    now
            });

        await dbContext.SaveChangesAsync();
    }

    private sealed class ChangeWatchPayload
    {
        public string ProviderName { get; set; } =
            string.Empty;

        public decimal AmountDifference { get; set; }

        public decimal AnnualizedImpact { get; set; }

        public string RecalculationStatus { get; set; } =
            string.Empty;

        public decimal? AlreadyPlanned { get; set; }

        public decimal? RemainingAmountToPlan { get; set; }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class StatementBillPlanningFactsGatewayTests
{
    [Fact]
    public async Task GetLatestAsync_IsOwnershipScoped_AndReturnsLatestStatementFacts()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-bill-facts-{Guid.NewGuid():N}")
                .Options;

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var unrequestedBillStreamId =
            Guid.NewGuid();

        var olderStatementId =
            Guid.NewGuid();

        var newestStatementId =
            Guid.NewGuid();

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BillStatements.AddRange(
                CreateStatement(
                    olderStatementId,
                    userId,
                    billStreamId,
                    new DateOnly(2026, 8, 31),
                    new DateOnly(2026, 9, 15),
                    90m),
                CreateStatement(
                    newestStatementId,
                    userId,
                    billStreamId,
                    new DateOnly(2026, 9, 30),
                    new DateOnly(2026, 10, 15),
                    105m),
                CreateStatement(
                    Guid.NewGuid(),
                    otherUserId,
                    billStreamId,
                    new DateOnly(2026, 10, 31),
                    new DateOnly(2026, 11, 15),
                    999m),
                CreateStatement(
                    Guid.NewGuid(),
                    userId,
                    unrequestedBillStreamId,
                    new DateOnly(2026, 9, 30),
                    new DateOnly(2026, 10, 20),
                    50m));

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new StatementBillPlanningFactsGateway(
                dbContext);

        var result =
            await gateway.GetLatestAsync(
                userId,
                [billStreamId]);

        var fact =
            Assert.Single(
                result);

        Assert.Equal(
            billStreamId,
            fact.Key);

        Assert.Equal(
            newestStatementId,
            fact.Value.StatementId);

        Assert.Equal(
            105m,
            fact.Value.Amount);

        Assert.Equal(
            new DateOnly(
                2026,
                10,
                15),
            fact.Value.DueDate);

        Assert.Equal(
            "USD",
            fact.Value.CurrencyCode);
    }

    [Fact]
    public async Task GetLatestAsync_RejectsEmptyBillStreamId()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-bill-facts-validation-{Guid.NewGuid():N}")
                .Options;

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new StatementBillPlanningFactsGateway(
                dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.GetLatestAsync(
                    Guid.NewGuid(),
                    [Guid.Empty]));
    }

    private static BillStatementEntity CreateStatement(
        Guid id,
        Guid userId,
        Guid billStreamId,
        DateOnly periodEnd,
        DateOnly dueDate,
        decimal totalAmount)
    {
        return new BillStatementEntity
        {
            Id = id,
            UserId = userId,
            BillStreamId = billStreamId,
            PeriodStart = periodEnd.AddMonths(-1),
            PeriodEnd = periodEnd,
            StatementDate = periodEnd,
            DueDate = dueDate,
            TotalAmount = totalAmount,
            CurrencyCode = "USD",
            RetrievedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

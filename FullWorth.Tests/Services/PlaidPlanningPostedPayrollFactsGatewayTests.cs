using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlaidPlanningPostedPayrollFactsGatewayTests
{
    [Fact]
    public async Task GetAsync_ReturnsOnlyOwnedPostedWageInflows_WithoutDescriptions()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-payroll-facts-{Guid.NewGuid():N}")
                .Options;

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var expectedTransactionId =
            Guid.NewGuid();

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BankTransactions.AddRange(
                CreateTransaction(
                    expectedTransactionId,
                    userId,
                    -1500m,
                    new DateOnly(2026, 9, 18),
                    isPending: false,
                    isRemoved: false,
                    "INCOME",
                    "INCOME_WAGES"),
                CreateTransaction(
                    Guid.NewGuid(),
                    userId,
                    -500m,
                    new DateOnly(2026, 9, 19),
                    isPending: true,
                    isRemoved: false,
                    "INCOME",
                    "INCOME_WAGES"),
                CreateTransaction(
                    Guid.NewGuid(),
                    userId,
                    -500m,
                    new DateOnly(2026, 9, 19),
                    isPending: false,
                    isRemoved: true,
                    "INCOME",
                    "INCOME_WAGES"),
                CreateTransaction(
                    Guid.NewGuid(),
                    userId,
                    500m,
                    new DateOnly(2026, 9, 19),
                    isPending: false,
                    isRemoved: false,
                    "INCOME",
                    "INCOME_WAGES"),
                CreateTransaction(
                    Guid.NewGuid(),
                    userId,
                    -100m,
                    new DateOnly(2026, 9, 19),
                    isPending: false,
                    isRemoved: false,
                    "INCOME",
                    "INCOME_INTEREST_EARNED"),
                CreateTransaction(
                    Guid.NewGuid(),
                    otherUserId,
                    -9000m,
                    new DateOnly(2026, 9, 18),
                    isPending: false,
                    isRemoved: false,
                    "INCOME",
                    "INCOME_WAGES"),
                CreateTransaction(
                    Guid.NewGuid(),
                    userId,
                    -1000m,
                    new DateOnly(2026, 8, 1),
                    isPending: false,
                    isRemoved: false,
                    "INCOME",
                    "INCOME_WAGES"));

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidPlanningPostedPayrollFactsGateway(
                dbContext);

        var result =
            await gateway.GetAsync(
                userId,
                new DateOnly(
                    2026,
                    9,
                    1),
                new DateOnly(
                    2026,
                    9,
                    30));

        var fact =
            Assert.Single(
                result);

        Assert.Equal(
            expectedTransactionId,
            fact.TransactionId);

        Assert.Equal(
            1500m,
            fact.Amount);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                18),
            fact.PostedDate);

        Assert.Equal(
            "USD",
            fact.CurrencyCode);

        Assert.DoesNotContain(
            fact.GetType().GetProperties(),
            property =>
                property.Name.Contains(
                    "Name",
                    StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains(
                    "Merchant",
                    StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains(
                    "Account",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAsync_RejectsOversizedDateRange()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"planning-payroll-facts-validation-{Guid.NewGuid():N}")
                .Options;

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidPlanningPostedPayrollFactsGateway(
                dbContext);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () =>
                gateway.GetAsync(
                    Guid.NewGuid(),
                    new DateOnly(
                        2025,
                        1,
                        1),
                    new DateOnly(
                        2026,
                        2,
                        1)));
    }

    private static BankTransactionEntity CreateTransaction(
        Guid id,
        Guid userId,
        decimal amount,
        DateOnly postedDate,
        bool isPending,
        bool isRemoved,
        string categoryPrimary,
        string categoryDetailed)
    {
        return new BankTransactionEntity
        {
            Id = id,
            UserId = userId,
            BankAccountId = Guid.NewGuid(),
            PlaidTransactionId = Guid.NewGuid().ToString("N"),
            Name = "Sensitive payroll description",
            MerchantName = "Sensitive employer name",
            Amount = amount,
            IsoCurrencyCode = "USD",
            PostedDate = postedDate,
            AuthorizedDate = postedDate,
            IsPending = isPending,
            IsRemoved = isRemoved,
            CategoryPrimary = categoryPrimary,
            CategoryDetailed = categoryDetailed,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

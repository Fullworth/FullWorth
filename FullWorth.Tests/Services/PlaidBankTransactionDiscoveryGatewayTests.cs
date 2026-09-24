using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlaidBankTransactionDiscoveryGatewayTests
{
    [Fact]
    public async Task GetDiscoveryTransactions_IsOwnershipScoped_AndExcludesRemoved()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            seed.BankTransactions.AddRange(
                CreateTransaction(
                    userId,
                    "Visible",
                    isRemoved:
                        false),

                CreateTransaction(
                    userId,
                    "Removed",
                    isRemoved:
                        true),

                CreateTransaction(
                    otherUserId,
                    "Other User",
                    isRemoved:
                        false));

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidBankTransactionDiscoveryGateway(
                dbContext);

        var results =
            await gateway.GetDiscoveryTransactionsAsync(
                userId);

        var result =
            Assert.Single(
                results);

        Assert.Equal(
            "Visible",
            result.Name);

        Assert.Equal(
            9.99m,
            result.Amount);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                1),
            result.PostedDate);
    }

    [Fact]
    public async Task GetDiscoveryTransactions_EmptyUserId_IsRejected()
    {
        await using var dbContext =
            new FullWorthDbContext(
                CreateOptions());

        var gateway =
            new PlaidBankTransactionDiscoveryGateway(
                dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.GetDiscoveryTransactionsAsync(
                    Guid.Empty));
    }

    private static DbContextOptions<FullWorthDbContext>
        CreateOptions()
    {
        return new DbContextOptionsBuilder<
                FullWorthDbContext>()
            .UseInMemoryDatabase(
                $"transaction-discovery-{Guid.NewGuid():N}")
            .Options;
    }

    private static BankTransactionEntity
        CreateTransaction(
            Guid userId,
            string name,
            bool isRemoved)
    {
        return new BankTransactionEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            BankAccountId =
                Guid.NewGuid(),

            PlaidTransactionId =
                Guid.NewGuid()
                    .ToString(
                        "N"),

            Name =
                name,

            MerchantName =
                name,

            Amount =
                9.99m,

            IsoCurrencyCode =
                "USD",

            PostedDate =
                new DateOnly(
                    2026,
                    9,
                    1),

            AuthorizedDate =
                new DateOnly(
                    2026,
                    9,
                    1),

            IsPending =
                false,

            IsRemoved =
                isRemoved,

            CreatedAtUtc =
                DateTimeOffset.UtcNow,

            UpdatedAtUtc =
                DateTimeOffset.UtcNow
        };
    }
}

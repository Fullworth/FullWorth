using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class PlaidBankTransactionDiscoveryGatewayTests
{
    [Fact]
    public async Task GetDiscoveryTransactions_IsOwnershipScoped_ExcludesRemoved_AndDoesNotExposeBillLinks()
    {
        var options =
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"transaction-discovery-{Guid.NewGuid():N}")
                .Options;

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
                    isRemoved: false,
                    billStreamId: Guid.NewGuid()),
                CreateTransaction(
                    userId,
                    "Removed",
                    isRemoved: true,
                    billStreamId: Guid.NewGuid()),
                CreateTransaction(
                    otherUserId,
                    "Other User",
                    isRemoved: false,
                    billStreamId: Guid.NewGuid()));

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

        Assert.DoesNotContain(
            result.GetType().GetProperties(),
            property =>
                property.Name.Contains(
                    "BillStream",
                    StringComparison.Ordinal));
    }

    private static BankTransactionEntity CreateTransaction(
        Guid userId,
        string name,
        bool isRemoved,
        Guid? billStreamId)
    {
        return new BankTransactionEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            BankAccountId = Guid.NewGuid(),
            BillStreamId = billStreamId,
            PlaidTransactionId = Guid.NewGuid().ToString("N"),
            Name = name,
            MerchantName = name,
            Amount = 9.99m,
            IsoCurrencyCode = "USD",
            PostedDate = new DateOnly(2026, 9, 1),
            AuthorizedDate = new DateOnly(2026, 9, 1),
            IsPending = false,
            IsRemoved = isRemoved,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

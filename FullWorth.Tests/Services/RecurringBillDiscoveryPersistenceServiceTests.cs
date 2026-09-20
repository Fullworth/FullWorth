using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FullWorth.Tests.Services;

public sealed class RecurringBillDiscoveryPersistenceServiceTests
{
    [Fact]
    public async Task StableMonthlyUnclassifiedService_IsPromotedAsOther()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud Service",
            9.99m,
            categoryPrimary: null,
            categoryDetailed: null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        Assert.Equal(
            1,
            result.BillStreamsCreated);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Other,
            billStream.Category);

        Assert.True(
            billStream.IsActive);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Equal(
                    billStream.Id,
                    transaction.BillStreamId));
    }

    [Fact]
    public async Task VariableKnownUtilityWithoutPlaidCategory_IsPromotedAsUtility()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Black Hills Energy",
            new DateOnly(2026, 1, 5),
            118.42m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Black Hills Energy",
            new DateOnly(2026, 2, 5),
            176.19m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Black Hills Energy",
            new DateOnly(2026, 3, 5),
            143.77m,
            null,
            null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        Assert.Equal(
            1,
            result.BillStreamsCreated);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Utility,
            billStream.Category);

        Assert.Equal(
            "Black Hills Energy",
            billStream.ProviderName);

        Assert.True(
            billStream.IsActive);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Equal(
                    billStream.Id,
                    transaction.BillStreamId));
    }

    [Fact]
    public async Task TwoMonthlyDigitalSubscriptionCharges_ArePromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Example Digital Subscription",
            new DateOnly(2026, 1, 12),
            8.99m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_DIGITAL_GOODS");

        AddTransaction(
            dbContext,
            userId,
            "Example Digital Subscription",
            new DateOnly(2026, 2, 12),
            8.99m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_DIGITAL_GOODS");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            1,
            result.BillsDiscovered);

        var billStream =
            Assert.Single(
                dbContext.BillStreams);

        Assert.Equal(
            BillCategory.Other,
            billStream.Category);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Equal(
                    billStream.Id,
                    transaction.BillStreamId));
    }

    [Fact]
    public async Task TwoMonthlyGeneralMerchandisePurchases_AreNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Example Store",
            new DateOnly(2026, 1, 12),
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        AddTransaction(
            dbContext,
            userId,
            "Example Store",
            new DateOnly(2026, 2, 12),
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Fact]
    public async Task StableMonthlyRestaurant_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Restaurant",
            20m,
            "FOOD_AND_DRINK",
            "FOOD_AND_DRINK_RESTAURANT");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);

        Assert.All(
            dbContext.BankTransactions,
            transaction =>
                Assert.Null(
                    transaction.BillStreamId));
    }

    [Fact]
    public async Task StableMonthlyGeneralMerchandiseWithoutSubscriptionEvidence_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Store",
            15m,
            "GENERAL_MERCHANDISE",
            "GENERAL_MERCHANDISE_OTHER_GENERAL_MERCHANDISE");

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Fact]
    public async Task VariableUnclassifiedMonthlySpending_IsNotPromoted()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 1, 5),
            10m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 2, 5),
            20m,
            null,
            null);

        AddTransaction(
            dbContext,
            userId,
            "Unknown Merchant",
            new DateOnly(2026, 3, 5),
            30m,
            null,
            null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            0,
            result.BillsDiscovered);

        Assert.Empty(
            dbContext.BillStreams);
    }

    [Fact]
    public async Task MultipleDiscoveries_LinkCorrectStreams_AndDeactivateStaleStream()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var now =
            DateTimeOffset.UtcNow;

        var staleStream =
            new BillStreamEntity
            {
                Id =
                    Guid.NewGuid(),

                UserId =
                    userId,

                ProviderName =
                    "Old Service",

                Category =
                    BillCategory.Other,

                Source =
                    BillStreamSource.AutomaticDiscovery,

                IsActive =
                    true,

                CreatedAtUtc =
                    now.AddMonths(-4),

                UpdatedAtUtc =
                    now.AddMonths(-1)
            };

        dbContext.BillStreams.Add(
            staleStream);

        var staleTransaction =
            AddTransaction(
                dbContext,
                userId,
                "Old Service",
                new DateOnly(2026, 1, 2),
                4.99m,
                null,
                null);

        staleTransaction.BillStreamId =
            staleStream.Id;

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud One",
            9.99m,
            null,
            null);

        AddMonthlyTransactions(
            dbContext,
            userId,
            "Example Cloud Two",
            19.99m,
            null,
            null);

        await dbContext.SaveChangesAsync();

        var service =
            new RecurringBillDiscoveryPersistenceService(
                dbContext);

        var result =
            await service.DiscoverAndSaveAsync(
                userId);

        Assert.Equal(
            2,
            result.BillsDiscovered);

        Assert.Equal(
            2,
            result.BillStreamsCreated);

        Assert.Equal(
            1,
            result.BillStreamsDeactivated);

        Assert.Equal(
            6,
            result.TransactionsLinked);

        Assert.Equal(
            1,
            result.TransactionsUnlinked);

        Assert.False(
            staleStream.IsActive);

        Assert.Null(
            staleTransaction.BillStreamId);

        var cloudOne =
            Assert.Single(
                dbContext.BillStreams
                    .Where(
                        stream =>
                            stream.ProviderName ==
                                "Example Cloud One"));

        var cloudTwo =
            Assert.Single(
                dbContext.BillStreams
                    .Where(
                        stream =>
                            stream.ProviderName ==
                                "Example Cloud Two"));

        Assert.All(
            dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.MerchantName ==
                            "Example Cloud One"),
            transaction =>
                Assert.Equal(
                    cloudOne.Id,
                    transaction.BillStreamId));

        Assert.All(
            dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.MerchantName ==
                            "Example Cloud Two"),
            transaction =>
                Assert.Equal(
                    cloudTwo.Id,
                    transaction.BillStreamId));
    }

    private static FullWorthDbContext CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"recurring-discovery-{Guid.NewGuid():N}")
                .Options);
    }

    private static void AddMonthlyTransactions(
        FullWorthDbContext dbContext,
        Guid userId,
        string merchantName,
        decimal amount,
        string? categoryPrimary,
        string? categoryDetailed)
    {
        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 1, 5),
            amount,
            categoryPrimary,
            categoryDetailed);

        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 2, 5),
            amount,
            categoryPrimary,
            categoryDetailed);

        AddTransaction(
            dbContext,
            userId,
            merchantName,
            new DateOnly(2026, 3, 5),
            amount,
            categoryPrimary,
            categoryDetailed);
    }

    private static BankTransactionEntity AddTransaction(
        FullWorthDbContext dbContext,
        Guid userId,
        string merchantName,
        DateOnly postedDate,
        decimal amount,
        string? categoryPrimary,
        string? categoryDetailed)
    {
        var transaction =
            new BankTransactionEntity
            {
                UserId =
                    userId,

                BankAccountId =
                    Guid.NewGuid(),

                PlaidTransactionId =
                    Guid.NewGuid()
                        .ToString("N"),

                Name =
                    merchantName,

                MerchantName =
                    merchantName,

                Amount =
                    amount,

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
                    categoryPrimary,

                CategoryDetailed =
                    categoryDetailed
            };

        dbContext.BankTransactions.Add(
            transaction);

        return transaction;
    }
}

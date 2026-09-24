using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
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
    }

    [Fact]
    public async Task StageAssignments_OtherUserTransaction_FailsAsNotFound()
    {
        var options =
            CreateOptions();

        var ownerUserId =
            Guid.NewGuid();

        var callerUserId =
            Guid.NewGuid();

        Guid transactionId;

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            var transaction =
                CreateTransaction(
                    ownerUserId,
                    "Private",
                    isRemoved:
                        false);

            transactionId =
                transaction.Id;

            seed.BankTransactions.Add(
                transaction);

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidBankTransactionDiscoveryGateway(
                dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () =>
                gateway.StageBillStreamAssignmentsAsync(
                    callerUserId,
                    [
                        new BankTransactionBillStreamAssignment(
                            transactionId,
                            null,
                            Guid.NewGuid())
                    ],
                    DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task StageAssignments_ChangedLink_FailsClosed()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        var currentBillStreamId =
            Guid.NewGuid();

        Guid transactionId;

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            var transaction =
                CreateTransaction(
                    userId,
                    "Concurrent",
                    isRemoved:
                        false);

            transaction.BillStreamId =
                currentBillStreamId;

            transactionId =
                transaction.Id;

            seed.BankTransactions.Add(
                transaction);

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidBankTransactionDiscoveryGateway(
                dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                gateway.StageBillStreamAssignmentsAsync(
                    userId,
                    [
                        new BankTransactionBillStreamAssignment(
                            transactionId,
                            ExpectedBillStreamId:
                                null,
                            BillStreamId:
                                Guid.NewGuid())
                    ],
                    DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task StageAssignments_DoesNotCommitBeforeOwningUnitOfWorkSaves()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        Guid transactionId;

        await using (var seed =
                     new FullWorthDbContext(
                         options))
        {
            var transaction =
                CreateTransaction(
                    userId,
                    "Atomic",
                    isRemoved:
                        false);

            transactionId =
                transaction.Id;

            seed.BankTransactions.Add(
                transaction);

            await seed.SaveChangesAsync();
        }

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidBankTransactionDiscoveryGateway(
                dbContext);

        await gateway.StageBillStreamAssignmentsAsync(
            userId,
            [
                new BankTransactionBillStreamAssignment(
                    transactionId,
                    ExpectedBillStreamId:
                        null,
                    BillStreamId:
                        billStreamId)
            ],
            DateTimeOffset.UtcNow);

        await using (var beforeCommit =
                     new FullWorthDbContext(
                         options))
        {
            var persistedBeforeCommit =
                await beforeCommit.BankTransactions
                    .AsNoTracking()
                    .SingleAsync(
                        transaction =>
                            transaction.Id ==
                                transactionId);

            Assert.Null(
                persistedBeforeCommit.BillStreamId);
        }

        await dbContext.SaveChangesAsync();

        await using var afterCommit =
            new FullWorthDbContext(
                options);

        var persistedAfterCommit =
            await afterCommit.BankTransactions
                .AsNoTracking()
                .SingleAsync(
                    transaction =>
                        transaction.Id ==
                            transactionId);

        Assert.Equal(
            billStreamId,
            persistedAfterCommit.BillStreamId);
    }

    [Fact]
    public async Task StageAssignments_DuplicateTransactionIds_AreRejected()
    {
        var options =
            CreateOptions();

        var userId =
            Guid.NewGuid();

        await using var dbContext =
            new FullWorthDbContext(
                options);

        var gateway =
            new PlaidBankTransactionDiscoveryGateway(
                dbContext);

        var transactionId =
            Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                gateway.StageBillStreamAssignmentsAsync(
                    userId,
                    [
                        new BankTransactionBillStreamAssignment(
                            transactionId,
                            null,
                            Guid.NewGuid()),

                        new BankTransactionBillStreamAssignment(
                            transactionId,
                            null,
                            Guid.NewGuid())
                    ],
                    DateTimeOffset.UtcNow));
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

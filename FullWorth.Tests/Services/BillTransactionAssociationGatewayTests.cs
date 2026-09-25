using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillTransactionAssociationGatewayTests
{
    [Fact]
    public async Task GetAsync_IsOwnershipScoped()
    {
        var options = CreateOptions();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var billStreamId = Guid.NewGuid();
        var otherBillStreamId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillStreams.AddRange(
                CreateBillStream(userId, billStreamId),
                CreateBillStream(otherUserId, otherBillStreamId));

            seed.BillTransactionAssociations.AddRange(
                new BillTransactionAssociationEntity
                {
                    UserId = userId,
                    BankTransactionId = transactionId,
                    BillStreamId = billStreamId
                },
                new BillTransactionAssociationEntity
                {
                    UserId = otherUserId,
                    BankTransactionId = Guid.NewGuid(),
                    BillStreamId = otherBillStreamId
                });

            await seed.SaveChangesAsync();
        }

        await using var dbContext = new FullWorthDbContext(options);
        var gateway = new BillTransactionAssociationGateway(dbContext);

        var result =
            Assert.Single(
                await gateway.GetAsync(
                    userId,
                    [transactionId]));

        Assert.Equal(transactionId, result.BankTransactionId);
        Assert.Equal(billStreamId, result.BillStreamId);
    }

    [Fact]
    public async Task StageAssignments_RejectsForeignBillStream()
    {
        var options = CreateOptions();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var foreignBillStreamId = Guid.NewGuid();

        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillStreams.Add(
                CreateBillStream(
                    otherUserId,
                    foreignBillStreamId));

            await seed.SaveChangesAsync();
        }

        await using var dbContext = new FullWorthDbContext(options);
        var gateway = new BillTransactionAssociationGateway(dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () =>
                gateway.StageAssignmentsAsync(
                    userId,
                    [
                        new BillTransactionAssociationAssignment(
                            Guid.NewGuid(),
                            null,
                            foreignBillStreamId)
                    ],
                    DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task StageAssignments_FailsClosedOnConcurrentChange()
    {
        var options = CreateOptions();
        var userId = Guid.NewGuid();
        var currentBillStreamId = Guid.NewGuid();
        var nextBillStreamId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillStreams.AddRange(
                CreateBillStream(userId, currentBillStreamId),
                CreateBillStream(userId, nextBillStreamId));

            seed.BillTransactionAssociations.Add(
                new BillTransactionAssociationEntity
                {
                    UserId = userId,
                    BankTransactionId = transactionId,
                    BillStreamId = currentBillStreamId
                });

            await seed.SaveChangesAsync();
        }

        await using var dbContext = new FullWorthDbContext(options);
        var gateway = new BillTransactionAssociationGateway(dbContext);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                gateway.StageAssignmentsAsync(
                    userId,
                    [
                        new BillTransactionAssociationAssignment(
                            transactionId,
                            ExpectedBillStreamId: null,
                            BillStreamId: nextBillStreamId)
                    ],
                    DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task StageAssignments_DoesNotCommitBeforeOwningUnitOfWorkSaves()
    {
        var options = CreateOptions();
        var userId = Guid.NewGuid();
        var billStreamId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using (var seed = new FullWorthDbContext(options))
        {
            seed.BillStreams.Add(
                CreateBillStream(
                    userId,
                    billStreamId));

            await seed.SaveChangesAsync();
        }

        await using var dbContext = new FullWorthDbContext(options);
        var gateway = new BillTransactionAssociationGateway(dbContext);

        await gateway.StageAssignmentsAsync(
            userId,
            [
                new BillTransactionAssociationAssignment(
                    transactionId,
                    null,
                    billStreamId)
            ],
            DateTimeOffset.UtcNow);

        await using (var beforeCommit = new FullWorthDbContext(options))
        {
            Assert.Empty(
                beforeCommit.BillTransactionAssociations);
        }

        await dbContext.SaveChangesAsync();

        await using var afterCommit = new FullWorthDbContext(options);
        var persisted =
            Assert.Single(
                afterCommit.BillTransactionAssociations);

        Assert.Equal(userId, persisted.UserId);
        Assert.Equal(transactionId, persisted.BankTransactionId);
        Assert.Equal(billStreamId, persisted.BillStreamId);
    }

    private static DbContextOptions<FullWorthDbContext> CreateOptions()
    {
        return new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(
                $"bill-transaction-association-{Guid.NewGuid():N}")
            .Options;
    }

    private static BillStreamEntity CreateBillStream(
        Guid userId,
        Guid id)
    {
        return new BillStreamEntity
        {
            Id = id,
            UserId = userId,
            ProviderName = $"Provider-{id:N}",
            Category = BillCategory.Other,
            Source = BillStreamSource.AutomaticDiscovery,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

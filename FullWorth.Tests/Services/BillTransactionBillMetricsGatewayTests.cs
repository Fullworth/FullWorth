using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillTransactionBillMetricsGatewayTests
{
    [Fact]
    public async Task GetMetrics_UsesOwnedLinksAndPreservesAmountSemantics()
    {
        await using var dbContext =
            new FullWorthDbContext(
                new DbContextOptionsBuilder<FullWorthDbContext>()
                    .UseInMemoryDatabase(
                        $"bill-transaction-metrics-{Guid.NewGuid():N}")
                    .Options);

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var oldestId =
            Guid.NewGuid();

        var previousId =
            Guid.NewGuid();

        var latestId =
            Guid.NewGuid();

        var foreignId =
            Guid.NewGuid();

        dbContext.BillTransactionLinks.AddRange(
            CreateLink(
                userId,
                billStreamId,
                oldestId),
            CreateLink(
                userId,
                billStreamId,
                previousId),
            CreateLink(
                userId,
                billStreamId,
                latestId),
            CreateLink(
                otherUserId,
                billStreamId,
                foreignId));

        await dbContext.SaveChangesAsync();

        var metricReader =
            new FakeMetricReader(
                [
                    CreateMetric(
                        oldestId,
                        10m,
                        new DateOnly(
                            2026,
                            7,
                            1)),
                    CreateMetric(
                        previousId,
                        20m,
                        new DateOnly(
                            2026,
                            8,
                            1)),
                    CreateMetric(
                        latestId,
                        40m,
                        new DateOnly(
                            2026,
                            9,
                            1)),
                    CreateMetric(
                        foreignId,
                        999m,
                        new DateOnly(
                            2026,
                            9,
                            2))
                ]);

        var gateway =
            new BillTransactionBillMetricsGateway(
                dbContext,
                metricReader);

        var results =
            await gateway.GetMetricsAsync(
                userId,
                [billStreamId]);

        var metrics =
            Assert.Single(
                results);

        Assert.Equal(
            billStreamId,
            metrics.Key);

        Assert.Equal(
            40m,
            metrics.Value.CurrentAmount);

        Assert.Equal(
            15m,
            metrics.Value.PreviousAverage);

        Assert.Equal(
            userId,
            metricReader.LastUserId);

        Assert.Equal(
            3,
            metricReader.LastTransactionIds.Count);

        Assert.DoesNotContain(
            foreignId,
            metricReader.LastTransactionIds);
    }

    private static BillTransactionLinkEntity CreateLink(
        Guid userId,
        Guid billStreamId,
        Guid transactionId)
    {
        return new BillTransactionLinkEntity
        {
            BankTransactionId =
                transactionId,

            UserId =
                userId,

            BillStreamId =
                billStreamId
        };
    }

    private static BankTransactionMetricRecord CreateMetric(
        Guid transactionId,
        decimal amount,
        DateOnly postedDate)
    {
        return new BankTransactionMetricRecord(
            transactionId,
            amount,
            postedDate,
            new DateTimeOffset(
                postedDate.Year,
                postedDate.Month,
                postedDate.Day,
                12,
                0,
                0,
                TimeSpan.Zero),
            IsPending:
                false,
            IsRemoved:
                false);
    }

    private sealed class FakeMetricReader(
        IReadOnlyCollection<BankTransactionMetricRecord> records)
        : IBankTransactionMetricReadGateway
    {
        public Guid LastUserId { get; private set; }

        public IReadOnlyCollection<Guid> LastTransactionIds { get; private set; } =
            [];

        public Task<IReadOnlyList<BankTransactionMetricRecord>>
            GetTransactionsAsync(
                Guid userId,
                IReadOnlyCollection<Guid> transactionIds,
                CancellationToken cancellationToken = default)
        {
            LastUserId =
                userId;

            LastTransactionIds =
                transactionIds.ToArray();

            var requestedIds =
                transactionIds.ToHashSet();

            IReadOnlyList<BankTransactionMetricRecord>
                result =
                    records
                        .Where(
                            record =>
                                requestedIds.Contains(
                                    record.TransactionId))
                        .ToArray();

            return Task.FromResult(
                result);
        }
    }
}

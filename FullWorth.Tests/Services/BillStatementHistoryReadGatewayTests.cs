using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BillStatementHistoryReadGatewayTests
{
    [Fact]
    public async Task GetAsync_ReturnsOnlyOwnedRowsInExpectedOrder()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var oldStatement =
            CreateStatement(
                userId,
                billStreamId,
                new DateOnly(
                    2026,
                    7,
                    1),
                new DateOnly(
                    2026,
                    7,
                    31),
                80m);

        var newStatement =
            CreateStatement(
                userId,
                billStreamId,
                new DateOnly(
                    2026,
                    8,
                    1),
                new DateOnly(
                    2026,
                    8,
                    31),
                95m);

        var foreignStatement =
            CreateStatement(
                otherUserId,
                billStreamId,
                new DateOnly(
                    2026,
                    9,
                    1),
                new DateOnly(
                    2026,
                    9,
                    30),
                999m);

        dbContext.BillStatements.AddRange(
            oldStatement,
            newStatement,
            foreignStatement);

        var olderChange =
            CreateChange(
                userId,
                billStreamId,
                oldStatement.Id,
                newStatement.Id,
                BillChangeType.TotalIncrease,
                new DateTimeOffset(
                    2026,
                    9,
                    1,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

        var newerChange =
            CreateChange(
                userId,
                billStreamId,
                newStatement.Id,
                newStatement.Id,
                BillChangeType.TotalDecrease,
                new DateTimeOffset(
                    2026,
                    9,
                    2,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

        var foreignChange =
            CreateChange(
                otherUserId,
                billStreamId,
                foreignStatement.Id,
                foreignStatement.Id,
                BillChangeType.TotalIncrease,
                new DateTimeOffset(
                    2026,
                    9,
                    3,
                    12,
                    0,
                    0,
                    TimeSpan.Zero));

        dbContext.BillChanges.AddRange(
            olderChange,
            newerChange,
            foreignChange);

        await dbContext.SaveChangesAsync();

        var gateway =
            new BillStatementHistoryReadGateway(
                dbContext);

        var snapshot =
            await gateway.GetAsync(
                userId,
                billStreamId);

        Assert.Equal(
            2,
            snapshot.Statements.Count);

        Assert.Equal(
            newStatement.Id,
            snapshot.Statements[0].Id);

        Assert.Equal(
            oldStatement.Id,
            snapshot.Statements[1].Id);

        Assert.DoesNotContain(
            snapshot.Statements,
            statement =>
                statement.Id ==
                    foreignStatement.Id);

        Assert.Equal(
            2,
            snapshot.Changes.Count);

        Assert.Equal(
            newerChange.Id,
            snapshot.Changes[0].Id);

        Assert.Equal(
            olderChange.Id,
            snapshot.Changes[1].Id);

        Assert.DoesNotContain(
            snapshot.Changes,
            change =>
                change.Id ==
                    foreignChange.Id);
    }

    [Fact]
    public async Task GetAsync_OtherUserForSameStreamId_ReturnsNoRows()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerUserId =
            Guid.NewGuid();

        var otherUserId =
            Guid.NewGuid();

        var billStreamId =
            Guid.NewGuid();

        var statement =
            CreateStatement(
                ownerUserId,
                billStreamId,
                new DateOnly(
                    2026,
                    8,
                    1),
                new DateOnly(
                    2026,
                    8,
                    31),
                95m);

        dbContext.BillStatements.Add(
            statement);

        dbContext.BillChanges.Add(
            CreateChange(
                ownerUserId,
                billStreamId,
                statement.Id,
                statement.Id,
                BillChangeType.TotalIncrease,
                DateTimeOffset.UtcNow));

        await dbContext.SaveChangesAsync();

        var gateway =
            new BillStatementHistoryReadGateway(
                dbContext);

        var snapshot =
            await gateway.GetAsync(
                otherUserId,
                billStreamId);

        Assert.Empty(
            snapshot.Statements);

        Assert.Empty(
            snapshot.Changes);
    }

    private static FullWorthDbContext
        CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<
                    FullWorthDbContext>()
                .UseInMemoryDatabase(
                    $"statement-history-{Guid.NewGuid():N}")
                .Options);
    }

    private static BillStatementEntity
        CreateStatement(
            Guid userId,
            Guid billStreamId,
            DateOnly periodStart,
            DateOnly periodEnd,
            decimal totalAmount)
    {
        var now =
            DateTimeOffset.UtcNow;

        return new BillStatementEntity
        {
            Id =
                Guid.NewGuid(),

            UserId =
                userId,

            BillStreamId =
                billStreamId,

            PeriodStart =
                periodStart,

            PeriodEnd =
                periodEnd,

            StatementDate =
                periodEnd.AddDays(
                    1),

            DueDate =
                periodEnd.AddDays(
                    21),

            TotalAmount =
                totalAmount,

            CurrencyCode =
                "USD",

            RetrievedAtUtc =
                now,

            CreatedAtUtc =
                now,

            UpdatedAtUtc =
                now
        };
    }

    private static BillChangeEntity
        CreateChange(
            Guid userId,
            Guid billStreamId,
            Guid previousStatementId,
            Guid currentStatementId,
            BillChangeType changeType,
            DateTimeOffset detectedAtUtc)
    {
        return new BillChangeEntity
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
                changeType,

            Confidence =
                BillChangeConfidence.Confirmed,

            Description =
                "Test change",

            PreviousAmount =
                80m,

            CurrentAmount =
                95m,

            AmountDifference =
                15m,

            AnnualizedImpact =
                180m,

            IsAcknowledged =
                false,

            DetectedAtUtc =
                detectedAtUtc,

            CreatedAtUtc =
                detectedAtUtc,

            UpdatedAtUtc =
                detectedAtUtc
        };
    }
}

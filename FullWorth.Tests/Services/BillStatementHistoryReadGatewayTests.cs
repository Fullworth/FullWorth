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

    [Fact]
    public async Task TiedDates_UseStableIdentifiersForStatementsAndChanges()
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var stream = Guid.NewGuid();
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var first = CreateStatement(owner, stream, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), 10m);
        var second = CreateStatement(owner, stream, first.PeriodStart, first.PeriodEnd, 20m);
        first.Id = low;
        second.Id = high;
        db.BillStatements.AddRange(first, second);
        var timestamp = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var firstChange = CreateChange(owner, stream, low, high, BillChangeType.TotalIncrease, timestamp);
        var secondChange = CreateChange(owner, stream, low, high, BillChangeType.TotalIncrease, timestamp);
        firstChange.Id = low;
        secondChange.Id = high;
        db.BillChanges.AddRange(firstChange, secondChange);
        await db.SaveChangesAsync();
        var gateway = new BillStatementHistoryReadGateway(db);
        var result = await gateway.GetAsync(owner, stream);
        Assert.Equal(new[] { high, low }, result.Statements.Select(statement => statement.Id));
        Assert.Equal(new[] { high, low }, result.Changes.Select(change => change.Id));
    }

    [Fact]
    public async Task GetAsync_SourceUploadIsLatestProcessedOwnedRecord()
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var bill = Guid.NewGuid();
        var otherBill = Guid.NewGuid();
        var statement = CreateStatement(owner, bill,
            new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 85m);
        db.BillStatements.Add(statement);

        var olderId = Guid.NewGuid();
        var latestId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        FullWorth.API.Data.Entities.BillStatementUploadEntity Upload(
            Guid uploadId, Guid userId, Guid streamId,
            BillStatementUploadStatus status, int minutesAgo) => new()
        {
            Id = uploadId,
            UserId = userId,
            BillStreamId = streamId,
            BillStatementId = statement.Id,
            Status = status,
            StorageKey = Guid.NewGuid().ToString("N"),
            MediaType = "application/pdf",
            FileExtension = ".pdf",
            SizeBytes = 24,
            CreatedAtUtc = now.AddMinutes(-minutesAgo),
            UpdatedAtUtc = now.AddMinutes(-minutesAgo)
        };

        db.BillStatementUploads.AddRange(
            Upload(olderId, owner, bill, BillStatementUploadStatus.Processed, 30),
            Upload(latestId, owner, bill, BillStatementUploadStatus.Processed, 10),
            Upload(Guid.NewGuid(), owner, bill, BillStatementUploadStatus.Failed, 1),
            Upload(Guid.NewGuid(), foreign, bill, BillStatementUploadStatus.Processed, 0),
            Upload(Guid.NewGuid(), owner, otherBill, BillStatementUploadStatus.Processed, 0));
        await db.SaveChangesAsync();

        var gateway = new BillStatementHistoryReadGateway(db);
        var snapshot = await gateway.GetAsync(owner, bill);
        Assert.Equal(latestId, Assert.Single(snapshot.Statements).SourceUploadId);
        Assert.Empty((await gateway.GetAsync(foreign, bill)).Statements);
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

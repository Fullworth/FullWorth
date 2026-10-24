using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace FullWorth.Tests.Services;

public sealed class StatementGraphFailureRecoveryTests
{
    public static IEnumerable<object[]> Failures()
    {
        foreach (var operation in new[] { "new", "duplicate-empty", "duplicate-enrich", "correction" })
        foreach (var mode in new[] { "database", "cancel", "unexpected", "stage" })
        foreach (var callerChanges in new[] { false, true })
            yield return [operation, mode, callerChanges];
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task FailedGraphAttempt_CannotLeakThroughLaterSave_AndRetryIsIdempotent(
        string operation, string mode, bool callerChanges)
    {
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var failure = new WriteFailure(mode);
        var plain = new DbContextOptionsBuilder<FullWorthDbContext>().UseInMemoryDatabase(name, root).Options;
        var options = new DbContextOptionsBuilder<FullWorthDbContext>().UseInMemoryDatabase(name, root)
            .AddInterceptors(failure).Options;
        var owner = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var stream = new BillStreamEntity { UserId = owner, ProviderName = "Fixture" };
        var otherStream = new BillStreamEntity { UserId = foreign, ProviderName = "Foreign fixture" };
        var modified = new BankConnectionEntity { UserId = owner, InstitutionName = "Original" };
        var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Deleted caller" };
        var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Added caller" };
        var start = new DateOnly(DateTime.UtcNow.Year + 1, 2, 1);
        var upload = new BillStatementUploadEntity { UserId = owner, BillStreamId = stream.Id,
            Status = BillStatementUploadStatus.Processing, StorageKey = "synthetic-only" };
        await using (var seed = new FullWorthDbContext(plain))
        {
            seed.AddRange(stream, otherStream, modified, deleted, upload);
            seed.Add(Statement(owner, stream.Id, start.AddMonths(-1), 100m));
            seed.Add(Statement(foreign, otherStream.Id, start, 777m));
            await seed.SaveChangesAsync();
            if (operation != "new")
                await Service(seed).PersistAsync(new BillStatementUploadEntity { UserId = owner, BillStreamId = stream.Id },
                    Parsed(start, 125m), operation == "correction" ? [new("Fixture fee", 25m, "Fee")] : []);
            foreach (var alert in await seed.BillAlerts.ToListAsync())
            { alert.IsRead = true; alert.IsDismissed = true; }
            await seed.SaveChangesAsync();
        }

        await using var db = new FullWorthDbContext(options);
        upload = await db.BillStatementUploads.SingleAsync(x => x.Id == upload.Id);
        modified = await db.BankConnections.SingleAsync(x => x.Id == modified.Id);
        deleted = await db.BankConnections.SingleAsync(x => x.Id == deleted.Id);
        if (callerChanges)
        {
            await db.BillStatements.ToListAsync();
            await db.BillLineItems.ToListAsync();
            await db.BillChanges.ToListAsync();
            await db.BillAlerts.ToListAsync();
            modified.InstitutionName = "Unsaved caller";
            db.Remove(deleted);
            db.Add(added);
        }
        var before = EvidenceAttemptTestSupport.Capture(db);
        string baseline;
        await using (var verify = new FullWorthDbContext(plain)) baseline = await FinancialState(verify);
        using var stop = new CancellationTokenSource();
        failure.Cancel = stop.Cancel;
        var alertGateway = new StagingFailure(new BillAlertReconciliationGateway(db), mode == "stage");
        var service = Service(db, alertGateway);
        failure.Armed = mode != "stage";
        var parsed = Parsed(start, operation == "correction" ? 150m : 125m);
        IReadOnlyList<BillStatementStructuredLineItem> items = operation == "duplicate-empty"
            ? [] : [new("Fixture fee", operation == "correction" ? 50m : 25m, "Fee")];
        var error = await Record.ExceptionAsync(() => service.PersistAsync(upload, parsed, items, stop.Token));
        if (mode == "cancel") Assert.True(error is OperationCanceledException, error?.ToString());
        else if (mode == "database") Assert.True(error is DbUpdateException, error?.ToString());
        else Assert.IsType<InvalidOperationException>(error);
        EvidenceAttemptTestSupport.AssertRestored(db, before);

        // Model the status/monitoring save which follows a failed attempt.
        await db.SaveChangesAsync();
        await using (var verify = new FullWorthDbContext(plain))
        {
            Assert.Equal(baseline, await FinancialState(verify));
            Assert.Equal(callerChanges ? "Unsaved caller" : "Original",
                (await verify.BankConnections.SingleAsync(x => x.Id == modified.Id)).InstitutionName);
            Assert.Equal(!callerChanges, await verify.BankConnections.AnyAsync(x => x.Id == deleted.Id));
            Assert.Equal(callerChanges, await verify.BankConnections.AnyAsync(x => x.Id == added.Id));
            Assert.Equal(BillStatementUploadStatus.Processing, (await verify.BillStatementUploads.SingleAsync()).Status);
            Assert.Null((await verify.BillStatementUploads.SingleAsync()).BillStatementId);
        }

        var retried = await service.PersistAsync(upload, parsed, items);
        var again = await service.PersistAsync(upload, parsed, items);
        Assert.Equal(retried.StatementId, again.StatementId);
        Assert.False(again.WasCreated);
        Assert.Equal(operation == "correction" ? 3 : 2,
            await db.BillStatements.CountAsync(x => x.UserId == owner));
        Assert.Equal(1, await db.BillChanges.CountAsync(x => x.UserId == owner));
        Assert.Equal(operation == "duplicate-empty" ? 0 : operation == "correction" ? 2 : 1,
            await db.BillLineItems.CountAsync(x => x.UserId == owner));
        Assert.Equal(BillStatementUploadStatus.Processed, upload.Status);
        Assert.Equal(retried.StatementId, upload.BillStatementId);
        var foreignStatement = await db.BillStatements.AsNoTracking().SingleAsync(x => x.UserId == foreign);
        Assert.Equal(777m, foreignStatement.TotalAmount);
        Assert.Equal(otherStream.Id, foreignStatement.BillStreamId);
    }

    private static BillStatementEntity Statement(Guid owner, Guid stream, DateOnly start, decimal amount) => new()
    {
        UserId = owner, BillStreamId = stream, PeriodStart = start,
        PeriodEnd = start.AddMonths(1).AddDays(-1), StatementDate = start.AddMonths(1),
        DueDate = start.AddMonths(1).AddDays(10), TotalAmount = amount, CurrencyCode = "USD"
    };
    private static BillStatementStructuredData Parsed(DateOnly start, decimal amount) => new(
        amount, start, start.AddMonths(1).AddDays(-1), start.AddMonths(1), start.AddMonths(1).AddDays(10),
        "USD", BillStatementStructuredDataConfidence.StrongEvidence, []);
    private static BillStatementPersistenceService Service(FullWorthDbContext db,
        IBillAlertReconciliationGateway? gateway = null)
    {
        var streams = new BillStreamReadGateway(db);
        var alerts = gateway ?? new BillAlertReconciliationGateway(db);
        return new(db, new BillStatementChangeDetectionService(db, streams, new BillStatementEvidenceAlertService(), alerts),
            new BillStatementPaymentDueAlertService(streams, alerts));
    }
    private static async Task<string> FinancialState(FullWorthDbContext db) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            Statements = await db.BillStatements.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.UserId, x.BillStreamId, x.PeriodStart, x.PeriodEnd, x.TotalAmount, x.CurrencyCode }).ToArrayAsync(),
            Lines = await db.BillLineItems.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.UserId, x.BillStatementId, x.Description, x.Amount }).ToArrayAsync(),
            Changes = await db.BillChanges.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.CurrentStatementId, x.AmountDifference, x.IsAcknowledged, x.UpdatedAtUtc }).ToArrayAsync(),
            Alerts = await db.BillAlerts.AsNoTracking().OrderBy(x => x.Id)
                .Select(x => new { x.Id, x.UserId, x.BillChangeId, x.Title, x.Message, x.IsRead, x.IsDismissed, x.UpdatedAtUtc }).ToArrayAsync()
        });

    private sealed class WriteFailure(string mode) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Armed = false;
            if (mode == "cancel") { Cancel!(); token.ThrowIfCancellationRequested(); }
            if (mode == "unexpected") throw new InvalidOperationException("Synthetic pre-commit failure.");
            throw new DbUpdateException("Synthetic pre-commit database failure.");
        }
    }
    private sealed class StagingFailure(IBillAlertReconciliationGateway inner, bool armed) : IBillAlertReconciliationGateway
    {
        private bool _armed = armed;
        public async Task StageReconciliationAsync(Guid userId, Guid billStreamId,
            IReadOnlyCollection<BillAlertReconciliationScope> scopes, IReadOnlyCollection<Guid> removeBillChangeIds,
            DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            await inner.StageReconciliationAsync(userId, billStreamId, scopes, removeBillChangeIds, now, cancellationToken);
            if (_armed) { _armed = false; throw new InvalidOperationException("Synthetic failure after staging."); }
        }
    }
}

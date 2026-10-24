using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Statements;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class StatementProcessingWriteRecoveryTests
{
    public static IEnumerable<object[]> Failures()
    {
        foreach (var outcome in new[] { "ocr", "incomplete", "review", "document-error", "persist" })
        foreach (var mode in new[] { "database", "cancel", "unexpected" })
        foreach (var uploaded in new[] { false, true })
            yield return [outcome, mode, uploaded];
        foreach (var mode in new[] { "database", "cancel", "unexpected" })
            yield return ["initial", mode, true];
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task DatabaseFailure_DoesNotBecomePermanentDocumentFailure_AndCanRetry(
        string outcome, string mode, bool uploaded)
    {
        await using var factory = new FullWorthApiFactory();
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var plain = new DbContextOptionsBuilder<FullWorthDbContext>().UseInMemoryDatabase(name, root).Options;
        var failure = new WriteFailure(plain, mode, outcome == "initial");
        var options = new DbContextOptionsBuilder<FullWorthDbContext>().UseInMemoryDatabase(name, root)
            .AddInterceptors(failure).Options;
        var storageRoot = Path.Combine(Path.GetTempPath(), "FullWorth-processing-recovery", Guid.NewGuid().ToString());
        var storage = new SecureBillStatementStorageService(Options.Create(new BillStatementStorageOptions
            { RootPath = storageRoot, MaxFileSizeBytes = 1024 * 1024 }),
            factory.Services.GetRequiredService<IWebHostEnvironment>());
        try
        {
            await using var db = new FullWorthDbContext(options);
            var owner = Guid.NewGuid();
            var stream = new BillStreamEntity { UserId = owner, ProviderName = "Fixture" };
            var foreignStream = new BillStreamEntity { UserId = Guid.NewGuid(), ProviderName = "Foreign" };
            var caller = new BankConnectionEntity { UserId = owner, InstitutionName = "Original" };
            using var bytes = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\nsynthetic fixture\n%%EOF"));
            var file = await storage.StoreAsync(owner, bytes, "fixture.pdf");
            var upload = new BillStatementUploadEntity { UserId = owner, BillStreamId = stream.Id,
                Status = uploaded ? BillStatementUploadStatus.Uploaded : BillStatementUploadStatus.Processing,
                StorageKey = file.StorageKey, MediaType = file.MediaType, FileExtension = file.FileExtension,
                SizeBytes = file.SizeBytes, UpdatedAtUtc = DateTimeOffset.Parse("2026-01-01T00:00:00Z") };
            var foreignUpload = new BillStatementUploadEntity { UserId = foreignStream.UserId,
                BillStreamId = foreignStream.Id, Status = BillStatementUploadStatus.Uploaded };
            db.AddRange(stream, foreignStream, caller, upload, foreignUpload);
            await db.SaveChangesAsync();
            caller.InstitutionName = "Unsaved caller";
            var streams = new BillStreamReadGateway(db);
            var alerts = new BillAlertReconciliationGateway(db);
            var persistence = new BillStatementPersistenceService(db,
                new BillStatementChangeDetectionService(db, streams, new BillStatementEvidenceAlertService(), alerts),
                new BillStatementPaymentDueAlertService(streams, alerts));
            var processor = new BillStatementProcessingService(db,
                new BillStatementDocumentTextReader(storage, new PdfReader(outcome), new Ocr()),
                new Extractor(outcome), new BillStatementValidationService(), persistence, streams,
                NullLogger<BillStatementProcessingService>.Instance);
            using var stop = new CancellationTokenSource();
            failure.Cancel = stop.Cancel;
            failure.Armed = true;
            var error = await Record.ExceptionAsync(() => processor.ProcessPendingUploadAsync(upload.Id, stop.Token));
            if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
            else if (mode == "database") Assert.IsType<DbUpdateException>(error);
            else Assert.IsType<InvalidOperationException>(error);
            Assert.Equal(1, failure.Failures);
            Assert.Equal(outcome == "initial" ? BillStatementUploadStatus.Uploaded : BillStatementUploadStatus.Processing,
                upload.Status);
            Assert.Equal(failure.BeforeFailureUpdatedAtUtc, upload.UpdatedAtUtc);
            Assert.Null(upload.BillStatementId);
            Assert.Empty(db.ChangeTracker.Entries<BillStatementEntity>());
            Assert.Empty(db.ChangeTracker.Entries<BillLineItemEntity>());
            Assert.Empty(db.ChangeTracker.Entries<BillChangeEntity>());
            Assert.Empty(db.ChangeTracker.Entries<BillAlertEntity>());
            Assert.Equal(uploaded && outcome != "initial" ? EntityState.Unchanged : EntityState.Modified,
                db.Entry(caller).State);
            if (!uploaded || outcome == "initial")
                Assert.Equal("Original", db.Entry(caller).OriginalValues.GetValue<string>(nameof(caller.InstitutionName)));

            await db.SaveChangesAsync();
            await using (var verify = new FullWorthDbContext(plain))
            {
                Assert.Equal(upload.Status, (await verify.BillStatementUploads.SingleAsync(x => x.Id == upload.Id)).Status);
                Assert.Equal(BillStatementUploadStatus.Uploaded,
                    (await verify.BillStatementUploads.SingleAsync(x => x.Id == foreignUpload.Id)).Status);
                Assert.Empty(await verify.BillStatements.ToListAsync());
                Assert.Empty(await verify.BillAlerts.ToListAsync());
                Assert.Equal("Unsaved caller", (await verify.BankConnections.SingleAsync()).InstitutionName);
            }
            await processor.ProcessPendingUploadAsync(upload.Id);
            var expected = outcome switch
            {
                "ocr" => BillStatementUploadStatus.NeedsOcr,
                "incomplete" or "review" => BillStatementUploadStatus.ReadyForParsing,
                "document-error" => BillStatementUploadStatus.Failed,
                _ => BillStatementUploadStatus.Processed
            };
            Assert.Equal(expected, upload.Status);
            Assert.Equal(expected == BillStatementUploadStatus.Processed ? 1 : 0, await db.BillStatements.CountAsync());
            if (expected == BillStatementUploadStatus.Processed)
            {
                Assert.Equal(100m, (await db.BillStatements.SingleAsync()).TotalAmount);
                Assert.NotNull(upload.BillStatementId);
            }
            using var stored = storage.OpenRead(owner, upload.StorageKey);
            Assert.True(stored.Length > 8);
        }
        finally { Directory.Delete(storageRoot, recursive: true); }
    }

    private sealed class WriteFailure(DbContextOptions<FullWorthDbContext> plain, string mode, bool initial)
        : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }
        public DateTimeOffset BeforeFailureUpdatedAtUtc { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken token = default)
        {
            if (!Armed) return result;
            var upload = data.Context!.ChangeTracker.Entries<BillStatementUploadEntity>()
                .Single(x => x.Entity.StorageKey.Length > 0).Entity;
            if (!initial && upload.Status == BillStatementUploadStatus.Processing) return result;
            Armed = false;
            Failures++;
            await using var read = new FullWorthDbContext(plain);
            BeforeFailureUpdatedAtUtc = (await read.BillStatementUploads.AsNoTracking()
                .SingleAsync(x => x.Id == upload.Id)).UpdatedAtUtc;
            if (mode == "cancel") { Cancel!(); token.ThrowIfCancellationRequested(); }
            if (mode == "unexpected") throw new InvalidOperationException("Synthetic pre-commit write failure.");
            throw new DbUpdateException("Synthetic pre-commit database failure.");
        }
    }
    private sealed class PdfReader(string outcome) : IPdfStatementTextExtractor
    {
        public BillStatementTextExtractionResult Extract(Stream pdf)
        {
            if (outcome == "document-error") throw new BillStatementTextExtractionException("Synthetic invalid document.");
            return new("Synthetic statement text fixture", 1, outcome == "ocr");
        }
    }
    private sealed class Ocr : IBillStatementOcrEngine
    {
        public BillStatementOcrResult TryExtract(Stream source, string mediaType, string fileExtension) =>
            BillStatementOcrResult.Failure(1);
    }
    private sealed class Extractor(string outcome) : IBillStatementExtractionService
    {
        public Task<BillStatementExtractionResult> ExtractAsync(BillStatementExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var start = new DateOnly(today.Year, today.Month, 1).AddMonths(-1);
            var parsed = new BillStatementStructuredData(outcome == "incomplete" ? null : 100m,
                start, start.AddMonths(1).AddDays(-1), start.AddMonths(1),
                outcome == "review" ? start.AddMonths(1).AddDays(-2) : today.AddDays(10),
                "USD", BillStatementStructuredDataConfidence.StrongEvidence, []);
            return Task.FromResult(new BillStatementExtractionResult(parsed, [new("Fixture service", 100m, "Service")],
                BillStatementExtractionSource.Deterministic, "fixture", []));
        }
    }
}

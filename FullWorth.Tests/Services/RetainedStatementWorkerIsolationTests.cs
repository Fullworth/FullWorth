using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Statements;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FullWorth.Tests.Services;

// Preserve the useful fault-isolation acceptance from superseded #845, but
// exercise today's scoped worker rather than restoring the old batch runner.
public sealed class RetainedStatementWorkerIsolationTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task DeferredFailures_DoNotStarveLaterRows_OrLeakTrackedWrites(
        int poisonCount, bool unrelatedCancellation)
    {
        var attempts = new ConcurrentQueue<(Guid Upload, Guid Context)>();
        await using var baseline = new FullWorthApiFactory();
        await using var factory = baseline.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(BillStatementProcessingBackgroundService)).ToArray())
                    services.Remove(descriptor);
                services.RemoveAll<IPdfStatementTextExtractor>();
                services.AddSingleton<IPdfStatementTextExtractor, SyntheticTextReader>();
                services.RemoveAll<IBillStatementExtractionService>();
                services.AddScoped<IBillStatementExtractionService>(provider =>
                    new FaultingExtraction(
                        provider.GetRequiredService<FullWorthDbContext>(),
                        new DeterministicBillStatementExtractionService(
                            provider.GetRequiredService<DeterministicBillStatementParser>(),
                            provider.GetRequiredService<DeterministicBillLineItemParser>()),
                        attempts, unrelatedCancellation));
            }));
        using var client = factory.CreateClient(); // Start only the disposable test host.
        var poisonIds = new List<Guid>();
        var healthyIds = new List<Guid>();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();
            for (var index = 0; index < poisonCount + 2; index++)
            {
                var owner = Guid.NewGuid();
                var bill = new BillStreamEntity
                {
                    UserId = owner, ProviderName = index < poisonCount ? "Poison fixture" : "Healthy fixture",
                    IsActive = true
                };
                db.BillStreams.Add(bill);
                using var stream = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\n%% synthetic\n"));
                var file = await storage.StoreAsync(owner, stream, "synthetic.pdf");
                var upload = new BillStatementUploadEntity
                {
                    UserId = owner, BillStreamId = bill.Id, StorageKey = file.StorageKey,
                    MediaType = file.MediaType, FileExtension = file.FileExtension,
                    Status = BillStatementUploadStatus.Uploaded,
                    CreatedAtUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(index)
                };
                db.BillStatementUploads.Add(upload);
                (index < poisonCount ? poisonIds : healthyIds).Add(upload.Id);
            }
            await db.SaveChangesAsync();
        }

        var worker = new BillStatementProcessingBackgroundService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new BillStatementProcessingSignal(),
            NullLogger<BillStatementProcessingBackgroundService>.Instance);
        var considered = 0;
        for (var pass = 0; pass < 4; pass++)
        {
            var count = await ProcessBatchAsync(worker, CancellationToken.None);
            considered += count;
            if (count == 0) break;
        }
        Assert.Equal(poisonCount + 2, considered);
        Assert.Equal(poisonCount + 2, attempts.Count);
        Assert.Equal(attempts.Count, attempts.Select(attempt => attempt.Context).Distinct().Count());
        Assert.Equal(0, await ProcessBatchAsync(worker, CancellationToken.None));

        await using var verification = factory.Services.CreateAsyncScope();
        var persisted = verification.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var statuses = await persisted.BillStatementUploads.AsNoTracking()
            .ToDictionaryAsync(upload => upload.Id, upload => upload.Status);
        Assert.All(poisonIds, id => Assert.Equal(BillStatementUploadStatus.Processing, statuses[id]));
        Assert.All(healthyIds, id => Assert.Equal(BillStatementUploadStatus.ReadyForParsing, statuses[id]));
        Assert.Empty(await persisted.BillAlerts.ToListAsync());
        Assert.Empty(await persisted.BillStatements.ToListAsync());
        Assert.Empty(await persisted.BillChanges.ToListAsync());
    }

    [Fact]
    public async Task RequestedCancellation_PropagatesBeforeAnyUploadAttempt()
    {
        await using var baseline = new FullWorthApiFactory();
        await using var factory = baseline.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services.Where(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(BillStatementProcessingBackgroundService)).ToArray())
                    services.Remove(descriptor);
            }));
        using var client = factory.CreateClient();
        var worker = new BillStatementProcessingBackgroundService(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new BillStatementProcessingSignal(),
            NullLogger<BillStatementProcessingBackgroundService>.Instance);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProcessBatchAsync(worker, cancellation.Token));
    }

    private static Task<int> ProcessBatchAsync(
        BillStatementProcessingBackgroundService worker, CancellationToken token) =>
        (Task<int>)typeof(BillStatementProcessingBackgroundService)
            .GetMethod("ProcessBatchAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(worker, [token])!;

    private sealed class SyntheticTextReader : IPdfStatementTextExtractor
    {
        public BillStatementTextExtractionResult Extract(Stream source) =>
            new("Synthetic extraction fixture without complete statement facts.", 1, false);
    }

    private sealed class FaultingExtraction(
        FullWorthDbContext db,
        IBillStatementExtractionService inner,
        ConcurrentQueue<(Guid Upload, Guid Context)> attempts,
        bool unrelatedCancellation) : IBillStatementExtractionService
    {
        public Task<BillStatementExtractionResult> ExtractAsync(
            BillStatementExtractionRequest request, CancellationToken cancellationToken = default)
        {
            var upload = Assert.Single(db.ChangeTracker.Entries<BillStatementUploadEntity>()).Entity;
            attempts.Enqueue((upload.Id, db.ContextId.InstanceId));
            Assert.DoesNotContain(db.ChangeTracker.Entries<BillAlertEntity>(),
                entry => entry.State == EntityState.Added);
            if (request.Hints.ExpectedProviderName == "Poison fixture")
            {
                // An unexpected failure after staging a write must not let the
                // next upload save that abandoned unit of work.
                db.BillAlerts.Add(new BillAlertEntity
                {
                    UserId = upload.UserId, BillStreamId = upload.BillStreamId,
                    Title = "Uncommitted synthetic sentinel", Message = "Must never persist"
                });
                if (unrelatedCancellation) throw new OperationCanceledException();
                throw new TimeoutException("Synthetic transient failure");
            }
            return inner.ExtractAsync(request, cancellationToken);
        }
    }
}

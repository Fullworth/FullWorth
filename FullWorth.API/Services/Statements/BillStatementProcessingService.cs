using System.Threading.Channels;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementProcessingSignal
{
    private readonly Channel<bool> _channel =
        Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite,
                AllowSynchronousContinuations = false
            });

    public void Notify()
    {
        _channel.Writer.TryWrite(true);
    }

    public async ValueTask WaitAsync(
        CancellationToken cancellationToken)
    {
        await _channel.Reader.ReadAsync(cancellationToken);
    }
}

public sealed class BillStatementProcessingService
{
    private readonly FullWorthDbContext _dbContext;
    private readonly BillStatementDocumentTextReader _documentTextReader;
    private readonly IBillStatementExtractionService _extractionService;
    private readonly BillStatementValidationService _validationService;
    private readonly BillStatementPersistenceService _persistenceService;
    private readonly IBillStreamReadGateway _billStreamGateway;
    private readonly ILogger<BillStatementProcessingService> _logger;

    public BillStatementProcessingService(
        FullWorthDbContext dbContext,
        BillStatementDocumentTextReader documentTextReader,
        IBillStatementExtractionService extractionService,
        BillStatementValidationService validationService,
        BillStatementPersistenceService persistenceService,
        IBillStreamReadGateway billStreamGateway,
        ILogger<BillStatementProcessingService> logger)
    {
        _dbContext = dbContext;
        _documentTextReader = documentTextReader;
        _extractionService = extractionService;
        _validationService = validationService;
        _persistenceService = persistenceService;
        _billStreamGateway = billStreamGateway;
        _logger = logger;
    }

    public async Task<int> ProcessPendingBatchAsync(
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxItems),
                "Batch size must be greater than zero.");
        }

        /*
         * Load the pending uploads as tracked entities once.
         *
         * The previous path projected IDs and immediately queried every
         * upload again before processing it, adding one redundant database
         * round trip per item in the batch.
         */
        var candidates = await _dbContext.BillStatementUploads
            .Where(upload =>
                upload.Status == BillStatementUploadStatus.Uploaded ||
                upload.Status == BillStatementUploadStatus.Processing)
            .OrderBy(upload => upload.CreatedAtUtc)
            .ThenBy(upload => upload.Id)
            .Take(maxItems)
            .ToListAsync(cancellationToken);

        foreach (var upload in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await ProcessUploadAsync(
                upload,
                cancellationToken);
        }

        return candidates.Count;
    }

    // Give the worker a single-item entrypoint for fresh per-upload EF scopes.
    public async Task ProcessPendingUploadAsync(
        Guid uploadId, CancellationToken cancellationToken = default)
    {
        if (uploadId == Guid.Empty)
            throw new ArgumentException("Upload identity is required.", nameof(uploadId));

        var upload = await _dbContext.BillStatementUploads
            .SingleOrDefaultAsync(item =>
                item.Id == uploadId &&
                (item.Status == BillStatementUploadStatus.Uploaded ||
                 item.Status == BillStatementUploadStatus.Processing),
                cancellationToken);

        if (upload is not null)
            await ProcessUploadAsync(upload, cancellationToken);
    }

    private async Task ProcessUploadAsync(
        BillStatementUploadEntity upload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (upload.Status is not BillStatementUploadStatus.Uploaded and not BillStatementUploadStatus.Processing)
            return;

        if (upload.Status == BillStatementUploadStatus.Uploaded)
            await SaveUploadStateAsync(upload, BillStatementUploadStatus.Processing, cancellationToken);

        BillStatementExtractionResult? structuredExtraction = null;
        BillStatementUploadStatus? nextStatus = null;
        Exception? documentError = null;
        try
        {
            var extraction = _documentTextReader.Read(upload.UserId, upload.StorageKey,
                upload.MediaType, upload.FileExtension);
            if (extraction.RequiresOcr)
                nextStatus = BillStatementUploadStatus.NeedsOcr;
            else
            {
                var bill = await _billStreamGateway.GetOwnedAsync(
                    upload.UserId, upload.BillStreamId, cancellationToken);
                if (bill is null)
                    throw new InvalidOperationException(
                        "The statement upload is not associated with an owned Bill Stream.");

                structuredExtraction = await _extractionService.ExtractAsync(
                    new BillStatementExtractionRequest(extraction.Text,
                        new BillStatementExtractionHints(bill.ProviderName, bill.Category.ToString())),
                    cancellationToken);
                if (!structuredExtraction.IsReadyForValidation)
                    nextStatus = BillStatementUploadStatus.ReadyForParsing;
                else
                {
                    var parsed = structuredExtraction.Statement;
                    var validation = _validationService.Validate(new BillStatementValidationInput(
                        parsed.TotalAmount, parsed.BillingPeriodStart, parsed.BillingPeriodEnd,
                        parsed.StatementDate, parsed.DueDate, parsed.CurrencyCode),
                        DateOnly.FromDateTime(DateTime.UtcNow));
                    if (!validation.IsValid || validation.RequiresReview)
                        nextStatus = BillStatementUploadStatus.ReadyForParsing;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is BillStatementTextExtractionException or IOException or
            UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            nextStatus = BillStatementUploadStatus.Failed;
            documentError = ex;
        }

        // Keep persistence failures outside the document-validation catch.
        // An unsuccessful database write does not prove the document invalid.
        if (nextStatus.HasValue)
        {
            await SaveUploadStateAsync(upload, nextStatus.Value, cancellationToken);
            if (documentError is not null)
                _logger.LogWarning(
                    "Bill statement upload {UploadId} failed during document evidence processing with {ExceptionType}.",
                    upload.Id, documentError.GetType().Name);
            return;
        }

        await _persistenceService.PersistAsync(upload, structuredExtraction!.Statement,
            structuredExtraction.LineItems, cancellationToken);
    }

    private async Task SaveUploadStateAsync(BillStatementUploadEntity upload,
        BillStatementUploadStatus status, CancellationToken cancellationToken)
    {
        var attempt = new StatementUnitOfWorkAttempt(_dbContext, upload);
        try
        {
            upload.Status = status;
            upload.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            attempt.Restore();
            throw;
        }
    }

}

/// <summary>
/// Process-local exception backoff. An unexpected infrastructure failure does
/// not mark an uploaded financial document as permanently invalid.
/// </summary>
public sealed class StatementProcessingFailureBackoff
{
    private static readonly TimeSpan Delay = TimeSpan.FromHours(1);
    private readonly Dictionary<Guid, DateTimeOffset> _deferredUntil = [];

    public Guid[] DeferredIds(DateTimeOffset now)
    {
        foreach (var id in _deferredUntil.Where(item => item.Value <= now)
            .Select(item => item.Key).ToArray())
            _deferredUntil.Remove(id);
        return _deferredUntil.Keys.ToArray();
    }

    public void Defer(Guid id, DateTimeOffset now)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Upload identity is required.", nameof(id));
        _deferredUntil[id] = now.Add(Delay);
    }
}

public sealed class BillStatementProcessingBackgroundService
    : BackgroundService
{
    private const int ProcessingBatchSize = 4;

    private static readonly TimeSpan ErrorRetryDelay =
        TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaintenanceInterval =
        TimeSpan.FromMinutes(5);

    private readonly StatementProcessingFailureBackoff _failureBackoff = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BillStatementProcessingSignal _signal;
    private readonly ILogger<BillStatementProcessingBackgroundService> _logger;

    public BillStatementProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        BillStatementProcessingSignal signal,
        ILogger<BillStatementProcessingBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcileAccountDeletionQuarantineAsync(
                    stoppingToken);

                var processedCount = await ProcessBatchAsync(
                    stoppingToken);

                if (processedCount > 0)
                {
                    continue;
                }

                await WaitForSignalOrMaintenanceAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    "The statement processing worker encountered {ExceptionType}.",
                    ex.GetType().Name);

                try
                {
                    await Task.Delay(
                        ErrorRetryDelay,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    private async Task<int> ProcessBatchAsync(
        CancellationToken cancellationToken)
    {
        var deferred = _failureBackoff.DeferredIds(DateTimeOffset.UtcNow);
        Guid[] ids;

        // Select the next nondeferred records without tracking their payloads.
        using (var lookupScope = _scopeFactory.CreateScope())
        {
            var db = lookupScope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            ids = await db.BillStatementUploads.AsNoTracking()
                .Where(upload =>
                    (upload.Status == BillStatementUploadStatus.Uploaded ||
                     upload.Status == BillStatementUploadStatus.Processing) &&
                    !deferred.Contains(upload.Id))
                .OrderBy(upload => upload.CreatedAtUtc)
                .ThenBy(upload => upload.Id)
                .Take(ProcessingBatchSize)
                .Select(upload => upload.Id)
                .ToArrayAsync(cancellationToken);
        }

        // Isolate every attempt in its own context. Unexpected failures must
        // neither poison EF tracking nor starve valid documents behind them.
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = _scopeFactory.CreateScope();
            try
            {
                var processor = scope.ServiceProvider
                    .GetRequiredService<BillStatementProcessingService>();
                await processor.ProcessPendingUploadAsync(id, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _failureBackoff.Defer(id, DateTimeOffset.UtcNow);
                // Never log user IDs, file paths, provider data, or statement content.
                _logger.LogWarning(
                    "A statement processing item was deferred after {ExceptionType}.",
                    ex.GetType().Name);
            }
        }

        return ids.Length;
    }

    private async Task ReconcileAccountDeletionQuarantineAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var userExistenceGateway =
            scope.ServiceProvider.GetRequiredService<IIdentityUserExistenceGateway>();

        var statementStorage =
            scope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

        var recoveryLogger =
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<AccountDeletionStatementQuarantineRecovery>();

        var recovery = new AccountDeletionStatementQuarantineRecovery(
            userExistenceGateway,
            statementStorage,
            recoveryLogger);

        await recovery.ReconcileAsync(cancellationToken);
    }

    private async Task WaitForSignalOrMaintenanceAsync(
        CancellationToken cancellationToken)
    {
        using var maintenanceCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);

        var signalTask =
            _signal.WaitAsync(
                    maintenanceCancellation.Token)
                .AsTask();

        var maintenanceTask =
            Task.Delay(
                MaintenanceInterval,
                maintenanceCancellation.Token);

        var completed = await Task.WhenAny(
            signalTask,
            maintenanceTask);

        maintenanceCancellation.Cancel();

        try
        {
            await completed;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            // The losing wait was canceled after the other one completed.
        }
    }
}

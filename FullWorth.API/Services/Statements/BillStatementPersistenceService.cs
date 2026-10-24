using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementPersistenceService
{
    private readonly FullWorthDbContext
        _dbContext;

    private readonly BillStatementChangeDetectionService
        _changeDetectionService;

    private readonly BillStatementPaymentDueAlertService
        _paymentDueAlertService;

    public BillStatementPersistenceService(
        FullWorthDbContext dbContext,
        BillStatementChangeDetectionService changeDetectionService,
        BillStatementPaymentDueAlertService paymentDueAlertService)
    {
        _dbContext =
            dbContext;

        _changeDetectionService =
            changeDetectionService;

        _paymentDueAlertService =
            paymentDueAlertService;
    }

    public async Task<BillStatementPersistenceResult> PersistAsync(
        BillStatementUploadEntity upload,
        BillStatementStructuredData parsedStatement,
        IReadOnlyList<BillStatementStructuredLineItem> parsedLineItems,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        cancellationToken.ThrowIfCancellationRequested();
        var attempt = new StatementUnitOfWorkAttempt(_dbContext, upload);
        try
        {
            return await PersistCoreAsync(upload, parsedStatement, parsedLineItems, cancellationToken);
        }
        catch
        {
            // Statements, line items, changes, alerts and completion are one
            // attempt. A later status/caller save must not flush a failed graph.
            attempt.Restore();
            throw;
        }
    }

    private async Task<BillStatementPersistenceResult>
        PersistCoreAsync(
            BillStatementUploadEntity upload,
            BillStatementStructuredData parsedStatement,
            IReadOnlyList<BillStatementStructuredLineItem> parsedLineItems,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            upload);

        ArgumentNullException.ThrowIfNull(
            parsedStatement);

        ArgumentNullException.ThrowIfNull(
            parsedLineItems);

        if (upload.UserId ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "The statement upload does not have an owner.");
        }

        if (upload.BillStreamId ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "The statement upload is not associated with a bill stream.");
        }

        if (!parsedStatement.IsReadyForPersistence ||
            !parsedStatement.TotalAmount.HasValue ||
            !parsedStatement.BillingPeriodStart.HasValue ||
            !parsedStatement.BillingPeriodEnd.HasValue)
        {
            throw new InvalidOperationException(
                "The parsed statement does not contain enough validated data to persist.");
        }

        var periodStart =
            parsedStatement
                .BillingPeriodStart
                .Value;

        var periodEnd =
            parsedStatement
                .BillingPeriodEnd
                .Value;

        var totalAmount =
            parsedStatement
                .TotalAmount
                .Value;

        var currencyCode =
            parsedStatement
                .CurrencyCode
                .Trim()
                .ToUpperInvariant();

        if (currencyCode.Length !=
            3)
        {
            throw new InvalidOperationException(
                "The parsed statement currency code is invalid.");
        }

        if (!StatementMoneyStoragePolicy.IsSupportedTotal(totalAmount) ||
            parsedLineItems.Any(item => !StatementMoneyStoragePolicy.IsSupportedSigned(item.Amount)))
        {
            throw new InvalidOperationException(
                "Statement amounts are outside the supported storage range or precision.");
        }

        var existingStatement =
            await _dbContext.BillStatements
                .Where(
                    statement =>
                        statement.UserId ==
                            upload.UserId &&
                        statement.BillStreamId ==
                            upload.BillStreamId &&
                        statement.PeriodStart ==
                            periodStart &&
                        statement.PeriodEnd ==
                            periodEnd &&
                        statement.StatementDate ==
                            parsedStatement.StatementDate &&
                        statement.DueDate ==
                            parsedStatement.DueDate &&
                        statement.TotalAmount ==
                            totalAmount &&
                        statement.CurrencyCode ==
                            currencyCode)
                .OrderBy(
                    statement =>
                        statement.CreatedAtUtc)
                .FirstOrDefaultAsync(
                    cancellationToken);

        var now =
            DateTimeOffset.UtcNow;

        var today =
            DateOnly.FromDateTime(
                now.UtcDateTime);

        if (existingStatement is not null)
        {
            IReadOnlyList<BillLineItemEntity>
                pendingLineItems =
                    [];

            if (parsedLineItems.Count >
                0)
            {
                var hasExistingLineItems =
                    await _dbContext.BillLineItems
                        .AnyAsync(
                            lineItem =>
                                lineItem.UserId ==
                                    upload.UserId &&
                                lineItem.BillStatementId ==
                                    existingStatement.Id,
                            cancellationToken);

                if (!hasExistingLineItems)
                {
                    pendingLineItems =
                        CreateLineItems(
                            existingStatement,
                            parsedLineItems,
                            now);

                    _dbContext.BillLineItems.AddRange(
                        pendingLineItems);
                }

                await _changeDetectionService
                    .ReconcileAsync(
                        upload.UserId,
                        upload.BillStreamId,
                        pendingStatement:
                            null,
                        pendingLineItems:
                            pendingLineItems,
                        cancellationToken:
                            cancellationToken);
            }

            await ReconcileLatestPaymentDueAsync(
                upload.UserId, upload.BillStreamId, existingStatement,
                today, now, cancellationToken);

            upload.BillStatementId =
                existingStatement.Id;

            upload.Status =
                BillStatementUploadStatus.Processed;

            upload.UpdatedAtUtc =
                now;

            await _dbContext.SaveChangesAsync(
                cancellationToken);

            return new BillStatementPersistenceResult(
                StatementId:
                    existingStatement.Id,

                WasCreated:
                    false);
        }

        var statement =
            new BillStatementEntity
            {
                UserId =
                    upload.UserId,

                BillStreamId =
                    upload.BillStreamId,

                PeriodStart =
                    periodStart,

                PeriodEnd =
                    periodEnd,

                StatementDate =
                    parsedStatement.StatementDate,

                DueDate =
                    parsedStatement.DueDate,

                TotalAmount =
                    totalAmount,

                CurrencyCode =
                    currencyCode,

                ProviderStatementId =
                    null,

                RetrievedAtUtc =
                    now,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        _dbContext.BillStatements.Add(
            statement);

        var lineItems =
            CreateLineItems(
                statement,
                parsedLineItems,
                now);

        if (lineItems.Count >
            0)
        {
            _dbContext.BillLineItems.AddRange(
                lineItems);
        }

        await _changeDetectionService
            .ReconcileAsync(
                upload.UserId,
                upload.BillStreamId,
                pendingStatement:
                    statement,
                pendingLineItems:
                    lineItems,
                cancellationToken:
                    cancellationToken);

        await ReconcileLatestPaymentDueAsync(
            upload.UserId, upload.BillStreamId, statement,
            today, now, cancellationToken);

        upload.BillStatementId =
            statement.Id;

        upload.Status =
            BillStatementUploadStatus.Processed;

        upload.UpdatedAtUtc =
            now;

        /*
         * Statement, line items, change detection, alert generation
         * and upload completion commit together.
         */
        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new BillStatementPersistenceResult(
            StatementId:
                statement.Id,

            WasCreated:
                true);
    }

    private async Task ReconcileLatestPaymentDueAsync(
        Guid userId,
        Guid billStreamId,
        BillStatementEntity pendingStatement,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var persistedLatest = await _dbContext.BillStatements
            .Where(item => item.UserId == userId && item.BillStreamId == billStreamId)
            .OrderByDescending(item => item.PeriodEnd)
            .ThenByDescending(item => item.RetrievedAtUtc)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Include the new unsaved statement in the same transaction. Uploading
        // old history must never replace the current statement's due reminder.
        var candidates = new List<BillStatementEntity> { pendingStatement };
        if (persistedLatest is not null)
        {
            candidates.Add(persistedLatest);
        }

        var latest = candidates
            .OrderByDescending(item => item.PeriodEnd)
            .ThenByDescending(item => item.RetrievedAtUtc)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ThenByDescending(item => item.Id)
            .First();

        await _paymentDueAlertService.ReconcileAsync(
            userId, billStreamId, latest.DueDate, latest.TotalAmount,
            latest.CurrencyCode, today, now, cancellationToken);
    }

    private static IReadOnlyList<BillLineItemEntity>
        CreateLineItems(
            BillStatementEntity statement,
            IReadOnlyList<BillStatementStructuredLineItem> parsedLineItems,
            DateTimeOffset now)
    {
        if (parsedLineItems.Count ==
            0)
        {
            return [];
        }

        var results =
            new List<BillLineItemEntity>(
                parsedLineItems.Count);

        for (var index = 0;
             index < parsedLineItems.Count;
             index++)
        {
            var parsed =
                parsedLineItems[index];

            results.Add(
                new BillLineItemEntity
                {
                    UserId =
                        statement.UserId,

                    BillStatementId =
                        statement.Id,

                    BillStatement =
                        statement,

                    Description =
                        parsed.Description,

                    Amount =
                        parsed.Amount,

                    Category =
                        parsed.Category,

                    SortOrder =
                        index,

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                });
        }

        return results.AsReadOnly();
    }
}

public sealed record BillStatementPersistenceResult(
    Guid StatementId,
    bool WasCreated);

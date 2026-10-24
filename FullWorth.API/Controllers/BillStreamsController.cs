using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace FullWorth.API.Controllers;

[ApiController]
[Route("api/bill-streams")]
[Authorize]
public sealed class BillStreamsController : ControllerBase
{
    private readonly FullWorthDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IBankTransactionBillMetricsGateway _transactionMetricsGateway;
    private readonly IBillStatementHistoryReadGateway _statementHistoryGateway;

    public BillStreamsController(
        FullWorthDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IBankTransactionBillMetricsGateway transactionMetricsGateway,
        IBillStatementHistoryReadGateway statementHistoryGateway)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _transactionMetricsGateway = transactionMetricsGateway;
        _statementHistoryGateway = statementHistoryGateway;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BillStreamResult>>>
        GetBillStreams(
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var query =
            _dbContext.BillStreams
                .AsNoTracking()
                .Where(stream =>
                    stream.UserId == userId);

        if (!includeInactive)
        {
            query =
                query.Where(stream =>
                    stream.IsActive);
        }

        var streams =
            await query
                .OrderBy(stream =>
                    stream.ProviderName)
                .ToListAsync(
                    cancellationToken);

        if (streams.Count == 0)
        {
            return Ok(
                Array.Empty<BillStreamResult>());
        }

        var streamIds =
            streams
                .Select(stream =>
                    stream.Id)
                .ToList();

        var metricsByStream =
            await _transactionMetricsGateway
                .GetMetricsAsync(
                    userId,
                    streamIds,
                    cancellationToken);

        var results =
            streams
                .Select(stream =>
                {
                    metricsByStream.TryGetValue(
                        stream.Id,
                        out var metrics);

                    return new BillStreamResult(
                        Id:
                            stream.Id,

                        ProviderName:
                            stream.ProviderName,

                        Category:
                            stream.Category.ToString(),

                        IsActive:
                            stream.IsActive,

                        CurrentAmount:
                            metrics?.CurrentAmount ??
                            0m,

                        PreviousAverage:
                            metrics?.PreviousAverage ??
                            0m)
                    {
                        CurrencyCode = metrics?.CurrencyCode,
                        Source = stream.Source.ToString(),
                        HasTransactionEvidence = metrics is not null,
                        HasMonthlyCadenceEvidence = metrics?.HasMonthlyCadenceEvidence ?? false
                    };
                })
                .ToList();

        return Ok(results);
    }

    [HttpGet("{billStreamId:guid}")]
    public async Task<ActionResult<BillStreamDetailResult>>
        GetBillStreamDetail(
            Guid billStreamId,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (billStreamId == Guid.Empty)
        {
            return NotFound();
        }

        var streamSnapshot =
            await _dbContext.BillStreams
                .AsNoTracking()
                .Where(
                    candidate =>
                        candidate.Id ==
                            billStreamId &&
                        candidate.UserId ==
                            userId)
                .Select(
                    candidate =>
                        new
                        {
                            candidate.Id,
                            candidate.ProviderName,
                            candidate.Category,
                            candidate.IsActive,
                            candidate.Source
                        })
                .SingleOrDefaultAsync(
                    cancellationToken);

        if (streamSnapshot is null)
        {
            return NotFound();
        }

        var metricsByStream =
            await _transactionMetricsGateway
                .GetMetricsAsync(
                    userId,
                    [billStreamId],
                    cancellationToken);

        metricsByStream.TryGetValue(
            billStreamId,
            out var streamMetrics);

        var currentAmount =
            streamMetrics?.CurrentAmount ??
            0m;

        var previousAverage =
            streamMetrics?.PreviousAverage ??
            0m;

        var statementHistory =
            await _statementHistoryGateway.GetAsync(
                userId,
                billStreamId,
                cancellationToken);

        var statements =
            statementHistory.Statements
                .Select(
                    statement =>
                        new BillStatementHistoryResult(
                            statement.Id,
                            statement.PeriodStart,
                            statement.PeriodEnd,
                            statement.StatementDate,
                            statement.DueDate,
                            statement.TotalAmount,
                            statement.CurrencyCode)
                        {
                            SourceUploadId = statement.SourceUploadId
                        })
                .ToList();

        var statementCurrencyById = statements.ToDictionary(statement => statement.Id,
            statement => statement.CurrencyCode);

        var changes =
            statementHistory.Changes
                .Select(
                    change =>
                        new BillChangeResult(
                            change.Id,
                            change.PreviousStatementId,
                            change.CurrentStatementId,
                            change.ChangeType,
                            change.Confidence,
                            change.Description,
                            change.PreviousAmount,
                            change.CurrentAmount,
                            change.AmountDifference,
                            change.AnnualizedImpact,
                            change.IsAcknowledged,
                            change.DetectedAtUtc)
                        {
                            CurrencyCode = statementCurrencyById.GetValueOrDefault(change.CurrentStatementId)
                        })
                .ToList();

        return Ok(
            new BillStreamDetailResult(
                Id:
                    streamSnapshot.Id,

                ProviderName:
                    streamSnapshot.ProviderName,

                Category:
                    streamSnapshot.Category.ToString(),

                IsActive:
                    streamSnapshot.IsActive,

                CurrentAmount:
                    currentAmount,

                PreviousAverage:
                    previousAverage,

                Statements:
                    statements,

                Changes:
                    changes)
            {
                CurrencyCode = streamMetrics?.CurrencyCode,
                Source = streamSnapshot.Source.ToString(),
                HasTransactionEvidence = streamMetrics is not null,
                HasMonthlyCadenceEvidence = streamMetrics?.HasMonthlyCadenceEvidence ?? false
            });
    }

    [HttpPost("{billStreamId:guid}/confirm")]
    [EnableRateLimiting("financial-refresh")]
    public async Task<IActionResult> ConfirmBillStream(
        Guid billStreamId, CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (billStreamId == Guid.Empty) return NotFound();
        var stream = await _dbContext.BillStreams.SingleOrDefaultAsync(
            candidate => candidate.Id == billStreamId && candidate.UserId == userId,
            cancellationToken);
        if (stream is null) return NotFound();
        if (!stream.IsActive || stream.Source != BillStreamSource.Manual)
        {
            stream.IsActive = true;
            stream.Source = BillStreamSource.Manual;
            stream.UpdatedAtUtc = DateTimeOffset.UtcNow;
            if (!await TrySaveConfirmationAsync(cancellationToken))
                return Conflict(new { message = "This bill changed while saving. Try again." });
        }
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<BillStreamResult>>
        CreateBillStream(
            CreateBillStreamRequest request,
            CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var providerName =
            request.ProviderName?.Trim();

        if (string.IsNullOrWhiteSpace(providerName))
        {
            return BadRequest(
                new
                {
                    message =
                        "Provider name is required."
                });
        }

        if (providerName.Length > 200)
        {
            return BadRequest(
                new
                {
                    message =
                        "Provider name is too long."
                });
        }

        if (!Enum.TryParse<BillCategory>(
                request.Category,
                ignoreCase: true,
                out var category) ||
            !Enum.IsDefined(category) ||
            !string.Equals(category.ToString(), request.Category?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                new
                {
                    message =
                        "Bill category is invalid."
                });
        }

        var normalizedProviderName =
            providerName.ToLower();

        var existingStream =
            await _dbContext.BillStreams
                .FirstOrDefaultAsync(
                    stream =>
                        stream.UserId == userId &&
                        stream.ProviderName.ToLower() ==
                        normalizedProviderName,
                    cancellationToken);

        if (existingStream is not null)
        {
            var changed =
                false;

            if (!existingStream.IsActive)
            {
                existingStream.IsActive =
                    true;

                changed =
                    true;
            }

            if (existingStream.Category ==
                    BillCategory.Unknown &&
                category != BillCategory.Unknown)
            {
                existingStream.Category =
                    category;

                changed =
                    true;
            }

            // An explicit create request confirms this user's obligation even
            // when it was first found automatically. Discovery must not later
            // undo that manual choice merely because imported evidence changes.
            if (existingStream.Source !=
                BillStreamSource.Manual)
            {
                existingStream.Source =
                    BillStreamSource.Manual;

                changed =
                    true;
            }

            if (changed)
            {
                existingStream.UpdatedAtUtc =
                    DateTimeOffset.UtcNow;

                if (!await TrySaveConfirmationAsync(cancellationToken))
                    return Conflict(new { message = "This bill changed while saving. Try again." });
            }

            return Ok(
                new BillStreamResult(
                    Id:
                        existingStream.Id,

                    ProviderName:
                        existingStream.ProviderName,

                    Category:
                        existingStream.Category.ToString(),

                    IsActive:
                        existingStream.IsActive,

                    CurrentAmount:
                        0m,

                    PreviousAverage:
                        0m) { Source = existingStream.Source.ToString() });
        }

        var now =
            DateTimeOffset.UtcNow;

        var stream =
            new BillStreamEntity
            {
                UserId =
                    userId,

                ProviderName =
                    providerName,

                Category =
                    category,

                Source =
                    BillStreamSource.Manual,

                IsActive =
                    true,

                CreatedAtUtc =
                    now,

                UpdatedAtUtc =
                    now
            };

        _dbContext.BillStreams.Add(
            stream);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Ok(
            new BillStreamResult(
                Id:
                    stream.Id,

                ProviderName:
                    stream.ProviderName,

                Category:
                    stream.Category.ToString(),

                IsActive:
                    stream.IsActive,

                CurrentAmount:
                    0m,

                PreviousAverage:
                    0m) { Source = stream.Source.ToString() });
    }

    private async Task<bool> TrySaveConfirmationAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    private bool TryGetUserId(
        out Guid userId)
    {
        var userIdText =
            _userManager.GetUserId(User);

        return Guid.TryParse(
            userIdText,
            out userId) && userId != Guid.Empty;
    }
}

public sealed record CreateBillStreamRequest(
    string ProviderName,
    string Category);

public sealed record BillStreamResult(
    Guid Id,
    string ProviderName,
    string Category,
    bool IsActive,
    decimal CurrentAmount,
    decimal PreviousAverage)
{
    public string? CurrencyCode { get; init; }
    public string Source { get; init; } = "Unknown";
    public bool HasTransactionEvidence { get; init; }
    public bool HasMonthlyCadenceEvidence { get; init; }
}

public sealed record BillStreamDetailResult(
    Guid Id,
    string ProviderName,
    string Category,
    bool IsActive,
    decimal CurrentAmount,
    decimal PreviousAverage,
    IReadOnlyList<BillStatementHistoryResult> Statements,
    IReadOnlyList<BillChangeResult> Changes)
{
    public string? CurrencyCode { get; init; }
    public string Source { get; init; } = "Unknown";
    public bool HasTransactionEvidence { get; init; }
    public bool HasMonthlyCadenceEvidence { get; init; }
}

public sealed record BillStatementHistoryResult(
    Guid Id,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    DateOnly? StatementDate,
    DateOnly? DueDate,
    decimal TotalAmount,
    string CurrencyCode)
{
    public Guid? SourceUploadId { get; init; }
}

public sealed record BillChangeResult(
    Guid Id,
    Guid? PreviousStatementId,
    Guid CurrentStatementId,
    string ChangeType,
    string Confidence,
    string Description,
    decimal PreviousAmount,
    decimal CurrentAmount,
    decimal AmountDifference,
    decimal AnnualizedImpact,
    bool IsAcknowledged,
    DateTimeOffset DetectedAtUtc)
{
    public string? CurrencyCode { get; init; }
}

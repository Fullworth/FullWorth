using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.Core.Models;
using FullWorth.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementChangeDetectionService
{
    private const int MaxDescriptionLength =
        950;

    private const int MaxAlertTitleLength =
        300;

    private const int MaxAlertMessageLength =
        2000;

    private readonly FullWorthDbContext
        _dbContext;

    private readonly BillAnalysisService
        _analysisService =
            new();

    private readonly BillStatementEvidenceAlertService
        _evidenceAlertService;

    private readonly IBillStreamReadGateway
        _billStreamGateway;

    private readonly IBillAlertReconciliationGateway
        _billAlertGateway;

    public BillStatementChangeDetectionService(
        FullWorthDbContext dbContext,
        IBillStreamReadGateway billStreamGateway,
        BillStatementEvidenceAlertService evidenceAlertService,
        IBillAlertReconciliationGateway billAlertGateway)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        ArgumentNullException.ThrowIfNull(
            billStreamGateway);

        ArgumentNullException.ThrowIfNull(
            evidenceAlertService);

        ArgumentNullException.ThrowIfNull(
            billAlertGateway);

        _dbContext =
            dbContext;

        _billStreamGateway =
            billStreamGateway;

        _evidenceAlertService =
            evidenceAlertService;

        _billAlertGateway =
            billAlertGateway;
    }

    public async Task<BillStatementChangeReconciliationResult>
        ReconcileAsync(
            Guid userId,
            Guid billStreamId,
            BillStatementEntity? pendingStatement = null,
            IReadOnlyList<BillLineItemEntity>? pendingLineItems = null,
            CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (billStreamId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        if (pendingStatement is not null &&
            (
                pendingStatement.UserId !=
                    userId ||
                pendingStatement.BillStreamId !=
                    billStreamId
            ))
        {
            throw new InvalidOperationException(
                "The pending statement does not belong to the requested bill stream.");
        }

        var billStream =
            await _billStreamGateway.GetOwnedAsync(
                userId,
                billStreamId,
                cancellationToken);

        if (billStream is null ||
            string.IsNullOrWhiteSpace(
                billStream.ProviderName))
        {
            throw new InvalidOperationException(
                "The owned bill stream could not be found.");
        }

        var providerName =
            billStream.ProviderName;

        var statements =
            await _dbContext.BillStatements
                .Where(
                    statement =>
                        statement.UserId ==
                            userId &&
                        statement.BillStreamId ==
                            billStreamId)
                .ToListAsync(
                    cancellationToken);

        if (pendingStatement is not null &&
            statements.All(
                statement =>
                    statement.Id !=
                    pendingStatement.Id))
        {
            statements.Add(
                pendingStatement);
        }

        var canonicalStatements =
            statements
                .GroupBy(
                    statement =>
                        new
                        {
                            statement.PeriodStart,
                            statement.PeriodEnd
                        })
                .Select(
                    group =>
                        group
                            .OrderByDescending(
                                statement =>
                                    statement.RetrievedAtUtc)
                            .ThenByDescending(
                                statement =>
                                    statement.CreatedAtUtc)
                            .ThenByDescending(
                                statement =>
                                    statement.Id)
                            .First())
                .OrderBy(
                    statement =>
                        statement.PeriodStart)
                .ThenBy(
                    statement =>
                        statement.PeriodEnd)
                .ThenBy(
                    statement =>
                        statement.Id)
                .ToList();

        var statementIds =
            canonicalStatements
                .Select(
                    statement =>
                        statement.Id)
                .ToArray();

        List<BillLineItemEntity> lineItems;

        if (statementIds.Length ==
            0)
        {
            lineItems =
                [];
        }
        else
        {
            lineItems =
                await _dbContext.BillLineItems
                    .AsNoTracking()
                    .Where(
                        lineItem =>
                            lineItem.UserId ==
                                userId &&
                            statementIds.Contains(
                                lineItem.BillStatementId))
                    .OrderBy(
                        lineItem =>
                            lineItem.SortOrder)
                    .ToListAsync(
                        cancellationToken);
        }

        if (pendingLineItems is not null)
        {
            foreach (var pendingLineItem in
                     pendingLineItems)
            {
                if (pendingLineItem.UserId !=
                        userId ||
                    !statementIds.Contains(
                        pendingLineItem.BillStatementId))
                {
                    throw new InvalidOperationException(
                        "A pending line item does not belong to the requested bill history.");
                }

                if (lineItems.All(
                        item =>
                            item.Id !=
                            pendingLineItem.Id))
                {
                    lineItems.Add(
                        pendingLineItem);
                }
            }
        }

        var lineItemsByStatement =
            lineItems
                .GroupBy(
                    lineItem =>
                        lineItem.BillStatementId)
                .ToDictionary(
                    group =>
                        group.Key,

                    group =>
                        (IReadOnlyList<BillLineItemEntity>)
                        group
                            .OrderBy(
                                lineItem =>
                                    lineItem.SortOrder)
                            .ToList()
                            .AsReadOnly());

        var existingChanges =
            await _dbContext.BillChanges
                .Where(
                    change =>
                        change.UserId ==
                            userId &&
                        change.BillStreamId ==
                            billStreamId &&
                        (
                            change.ChangeType ==
                                BillChangeType.TotalIncrease ||
                            change.ChangeType ==
                                BillChangeType.TotalDecrease
                        ))
                .ToListAsync(
                    cancellationToken);

        var desiredChanges =
            BuildDesiredChanges(
                providerName,
                canonicalStatements,
                lineItemsByStatement);

        var existingByPair =
            new Dictionary<
                StatementPair,
                BillChangeEntity>();

        var duplicateExistingChanges =
            new List<BillChangeEntity>();

        foreach (var existingChange in
                 existingChanges)
        {
            if (!existingChange
                    .PreviousStatementId
                    .HasValue)
            {
                duplicateExistingChanges.Add(
                    existingChange);

                continue;
            }

            var pair =
                new StatementPair(
                    existingChange
                        .PreviousStatementId
                        .Value,

                    existingChange
                        .CurrentStatementId);

            if (!existingByPair.TryAdd(
                    pair,
                    existingChange))
            {
                duplicateExistingChanges.Add(
                    existingChange);
            }
        }

        var createdCount =
            0;

        var updatedCount =
            0;

        var now =
            DateTimeOffset.UtcNow;

        var activeChanges =
            new List<BillChangeEntity>(
                desiredChanges.Count);

        foreach (var desiredChange in
                 desiredChanges)
        {
            var pair =
                new StatementPair(
                    desiredChange
                        .PreviousStatement
                        .Id,

                    desiredChange
                        .CurrentStatement
                        .Id);

            if (!existingByPair.TryGetValue(
                    pair,
                    out var existingChange))
            {
                var newChange =
                    CreateChangeEntity(
                        userId,
                        billStreamId,
                        desiredChange,
                        now);

                _dbContext.BillChanges.Add(
                    newChange);

                activeChanges.Add(
                    newChange);

                createdCount++;

                continue;
            }

            existingByPair.Remove(
                pair);

            if (ApplyDesiredValues(
                    existingChange,
                    desiredChange,
                    now))
            {
                updatedCount++;
            }

            activeChanges.Add(
                existingChange);
        }

        var changesToRemove =
            duplicateExistingChanges
                .Concat(
                    existingByPair.Values)
                .DistinctBy(
                    change =>
                        change.Id)
                .ToList();

        var alertScopes =
            new List<BillAlertReconciliationScope>(
                activeChanges.Count * 2);

        foreach (var activeChange in
                 activeChanges)
        {
            var changeAlert =
                BuildDesiredAlert(
                    providerName,
                    activeChange);

            alertScopes.Add(
                new BillAlertReconciliationScope(
                    BillChangeId:
                        activeChange.Id,

                    ManagedAlertTypes:
                        [
                            BillAlertContractType.BillIncrease,
                            BillAlertContractType.BillDecrease
                        ],

                    DesiredAlerts:
                        changeAlert is null
                            ? []
                            : [changeAlert],

                    Mode:
                        BillAlertReconciliationMode.SingleManagedSlot));

            IReadOnlyList<BillLineItemEntity>
                previousEvidence =
                    [];

            IReadOnlyList<BillLineItemEntity>
                currentEvidence =
                    [];

            if (activeChange
                    .PreviousStatementId
                    .HasValue &&
                lineItemsByStatement.TryGetValue(
                    activeChange
                        .PreviousStatementId
                        .Value,
                    out var previousItems))
            {
                previousEvidence =
                    previousItems;
            }

            if (lineItemsByStatement.TryGetValue(
                    activeChange.CurrentStatementId,
                    out var currentItems))
            {
                currentEvidence =
                    currentItems;
            }

            var evidenceAlerts =
                _evidenceAlertService
                    .BuildDesiredAlerts(
                        userId,
                        billStreamId,
                        providerName,
                        activeChange,
                        previousEvidence,
                        currentEvidence);

            alertScopes.Add(
                new BillAlertReconciliationScope(
                    BillChangeId:
                        activeChange.Id,

                    ManagedAlertTypes:
                        [
                            BillAlertContractType.NewFee,
                            BillAlertContractType.RemovedDiscount
                        ],

                    DesiredAlerts:
                        evidenceAlerts,

                    Mode:
                        BillAlertReconciliationMode.ReplaceManagedSet));
        }

        await _billAlertGateway
            .StageReconciliationAsync(
                userId,
                billStreamId,
                alertScopes,
                changesToRemove
                    .Select(
                        change =>
                            change.Id)
                    .ToArray(),
                now,
                cancellationToken);

        if (changesToRemove.Count >
            0)
        {
            _dbContext.BillChanges.RemoveRange(
                changesToRemove);
        }

        return new BillStatementChangeReconciliationResult(
            CreatedCount:
                createdCount,

            UpdatedCount:
                updatedCount,

            RemovedCount:
                changesToRemove.Count);
    }

    private IReadOnlyList<DesiredBillChange>
        BuildDesiredChanges(
            string providerName,
            IReadOnlyList<BillStatementEntity> statements,
            IReadOnlyDictionary<
                Guid,
                IReadOnlyList<BillLineItemEntity>>
                lineItemsByStatement)
    {
        if (statements.Count <
            2)
        {
            return [];
        }

        var desiredChanges =
            new List<DesiredBillChange>(
                statements.Count - 1);

        for (var index = 1;
             index < statements.Count;
             index++)
        {
            var previous =
                statements[index - 1];

            var current =
                statements[index];

            if (!string.Equals(
                    previous.CurrencyCode,
                    current.CurrencyCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lineItemsByStatement.TryGetValue(
                previous.Id,
                out var previousLineItems);

            lineItemsByStatement.TryGetValue(
                current.Id,
                out var currentLineItems);

            var previousDomainStatement =
                CreateDomainStatement(
                    providerName,
                    previous,
                    previousLineItems ??
                        []);

            var currentDomainStatement =
                CreateDomainStatement(
                    providerName,
                    current,
                    currentLineItems ??
                        []);

            var analysis =
                _analysisService.Analyze(
                    previousDomainStatement,
                    currentDomainStatement);

            var totalComparison =
                analysis.Comparison
                    .TotalComparison;

            if (totalComparison.MonthlyChange ==
                0m)
            {
                continue;
            }

            var changeType =
                totalComparison.MonthlyChange >
                0m
                    ? BillChangeType.TotalIncrease
                    : BillChangeType.TotalDecrease;

            desiredChanges.Add(
                new DesiredBillChange(
                    PreviousStatement:
                        previous,

                    CurrentStatement:
                        current,

                    ChangeType:
                        changeType,

                    PreviousAmount:
                        totalComparison.PreviousAmount,

                    CurrentAmount:
                        totalComparison.CurrentAmount,

                    AmountDifference:
                        totalComparison.MonthlyChange,

                    AnnualizedImpact:
                        totalComparison.AnnualChange,

                    Description:
                        BuildDescription(
                            analysis)));
        }

        return desiredChanges.AsReadOnly();
    }

    private static BillStatement
        CreateDomainStatement(
            string providerName,
            BillStatementEntity statement,
            IReadOnlyList<BillLineItemEntity> lineItems)
    {
        return new BillStatement(
            providerName:
                providerName,

            billingPeriodStart:
                statement.PeriodStart,

            billingPeriodEnd:
                statement.PeriodEnd,

            totalAmount:
                new BillAmount(
                    statement.TotalAmount),

            lineItems:
                lineItems.Select(
                    lineItem =>
                        new BillLineItem(
                            lineItem.Description,
                            lineItem.Amount)));
    }

    private static string BuildDescription(
        BillAnalysisResult analysis)
    {
        var summary =
            analysis.Explanation
                .Summary;

        var meaningfulChanges =
            analysis.Explanation
                .Changes
                .Take(
                    4)
                .ToList();

        if (meaningfulChanges.Count ==
            0)
        {
            return TruncateDescription(
                $"{summary} The provider statements confirm the amount change; FullWorth has not identified the cause yet.");
        }

        var evidence =
            string.Join(
                " ",
                meaningfulChanges.Select(
                    change =>
                        change.Description));

        if (Math.Abs(
                analysis.Explanation
                    .UnexplainedChange) <=
            0.01m)
        {
            return TruncateDescription(
                $"{summary} Why: {evidence}");
        }

        var unexplainedAmount =
            Math.Abs(
                analysis.Explanation
                    .UnexplainedChange);

        return TruncateDescription(
            $"{summary} Evidence found: {evidence} {FormatMoney(unexplainedAmount)}/month remains unexplained.");
    }

    private static BillAlertDesiredState?
        BuildDesiredAlert(
            string providerName,
            BillChangeEntity change)
    {
        var monthlyImpact =
            Math.Abs(
                change.AmountDifference);

        var annualImpact =
            Math.Abs(
                change.AnnualizedImpact);

        switch (change.ChangeType)
        {
            case BillChangeType.TotalIncrease:
                {
                    var title =
                        TruncateAlertValue(
                            $"{providerName} increased by {FormatMoney(monthlyImpact)}/month",
                            MaxAlertTitleLength);

                    var message =
                        TruncateAlertValue(
                            $"{FormatMoney(change.PreviousAmount)} → {FormatMoney(change.CurrentAmount)}. +{FormatMoney(monthlyImpact)}/month · +{FormatMoney(annualImpact)}/year. {change.Description}",
                            MaxAlertMessageLength);

                    return new BillAlertDesiredState(
                        AlertType:
                            BillAlertContractType.BillIncrease,

                        Severity:
                            BillAlertContractSeverity.Warning,

                        Title:
                            title,

                        Message:
                            message);
                }

            case BillChangeType.TotalDecrease:
                {
                    var title =
                        TruncateAlertValue(
                            $"{providerName} decreased by {FormatMoney(monthlyImpact)}/month",
                            MaxAlertTitleLength);

                    var message =
                        TruncateAlertValue(
                            $"{FormatMoney(change.PreviousAmount)} → {FormatMoney(change.CurrentAmount)}. {FormatMoney(monthlyImpact)}/month less · {FormatMoney(annualImpact)}/year less. {change.Description}",
                            MaxAlertMessageLength);

                    return new BillAlertDesiredState(
                        AlertType:
                            BillAlertContractType.BillDecrease,

                        Severity:
                            BillAlertContractSeverity.Info,

                        Title:
                            title,

                        Message:
                            message);
                }

            default:
                return null;
        }
    }

    private static BillChangeEntity
        CreateChangeEntity(
            Guid userId,
            Guid billStreamId,
            DesiredBillChange desiredChange,
            DateTimeOffset now)
    {
        return new BillChangeEntity
        {
            UserId =
                userId,

            BillStreamId =
                billStreamId,

            PreviousStatementId =
                desiredChange
                    .PreviousStatement
                    .Id,

            CurrentStatementId =
                desiredChange
                    .CurrentStatement
                    .Id,

            PreviousStatement =
                desiredChange
                    .PreviousStatement,

            CurrentStatement =
                desiredChange
                    .CurrentStatement,

            ChangeType =
                desiredChange.ChangeType,

            Confidence =
                BillChangeConfidence.Confirmed,

            Description =
                desiredChange.Description,

            PreviousAmount =
                desiredChange.PreviousAmount,

            CurrentAmount =
                desiredChange.CurrentAmount,

            AmountDifference =
                desiredChange.AmountDifference,

            AnnualizedImpact =
                desiredChange.AnnualizedImpact,

            IsAcknowledged =
                false,

            DetectedAtUtc =
                now,

            CreatedAtUtc =
                now,

            UpdatedAtUtc =
                now
        };
    }

    private static bool ApplyDesiredValues(
        BillChangeEntity existingChange,
        DesiredBillChange desiredChange,
        DateTimeOffset now)
    {
        var changed =
            false;

        if (existingChange.ChangeType !=
            desiredChange.ChangeType)
        {
            existingChange.ChangeType =
                desiredChange.ChangeType;

            changed =
                true;
        }

        if (existingChange.Confidence !=
            BillChangeConfidence.Confirmed)
        {
            existingChange.Confidence =
                BillChangeConfidence.Confirmed;

            changed =
                true;
        }

        if (!string.Equals(
                existingChange.Description,
                desiredChange.Description,
                StringComparison.Ordinal))
        {
            existingChange.Description =
                desiredChange.Description;

            changed =
                true;
        }

        if (existingChange.PreviousAmount !=
            desiredChange.PreviousAmount)
        {
            existingChange.PreviousAmount =
                desiredChange.PreviousAmount;

            changed =
                true;
        }

        if (existingChange.CurrentAmount !=
            desiredChange.CurrentAmount)
        {
            existingChange.CurrentAmount =
                desiredChange.CurrentAmount;

            changed =
                true;
        }

        if (existingChange.AmountDifference !=
            desiredChange.AmountDifference)
        {
            existingChange.AmountDifference =
                desiredChange.AmountDifference;

            changed =
                true;
        }

        if (existingChange.AnnualizedImpact !=
            desiredChange.AnnualizedImpact)
        {
            existingChange.AnnualizedImpact =
                desiredChange.AnnualizedImpact;

            changed =
                true;
        }

        if (!changed)
        {
            return false;
        }

        existingChange.DetectedAtUtc =
            now;

        existingChange.UpdatedAtUtc =
            now;

        return true;
    }

    private static string FormatMoney(
        decimal amount)
    {
        return
            $"${amount:0.00}";
    }

    private static string TruncateDescription(
        string value)
    {
        if (value.Length <=
            MaxDescriptionLength)
        {
            return value;
        }

        return
            value[..(MaxDescriptionLength - 1)]
            + "…";
    }

    private static string TruncateAlertValue(
        string value,
        int maximumLength)
    {
        if (value.Length <=
            maximumLength)
        {
            return value;
        }

        return
            value[..(maximumLength - 1)]
            + "…";
    }

    private readonly record struct StatementPair(
        Guid PreviousStatementId,
        Guid CurrentStatementId);

    private sealed record DesiredBillChange(
        BillStatementEntity PreviousStatement,
        BillStatementEntity CurrentStatement,
        BillChangeType ChangeType,
        decimal PreviousAmount,
        decimal CurrentAmount,
        decimal AmountDifference,
        decimal AnnualizedImpact,
        string Description);

}

public sealed record BillStatementChangeReconciliationResult(
    int CreatedCount,
    int UpdatedCount,
    int RemovedCount);
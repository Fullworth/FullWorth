using System.Text.RegularExpressions;

namespace FullWorth.API.Services.Statements;

/// <summary>
/// Deterministically reconciles already-bounded chunk candidates.
///
/// Scalar facts must agree across chunks. Line items are reconciled only when
/// their description and amount evidence can be tied to one unique physical
/// source region inside the chunk that produced them. FullWorth never
/// deduplicates line items by description/amount alone because identical
/// charges may legitimately occur more than once on the same statement.
/// </summary>
public sealed class BillStatementAiChunkCandidateReconciler
{
    private readonly BillStatementAiCandidateValidator
        _candidateValidator;

    public BillStatementAiChunkCandidateReconciler(
        BillStatementAiCandidateValidator candidateValidator)
    {
        _candidateValidator =
            candidateValidator ??
            throw new ArgumentNullException(
                nameof(candidateValidator));
    }

    public BillStatementAiChunkCandidateReconciliationResult Reconcile(
        string documentText,
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates)
    {
        ArgumentNullException.ThrowIfNull(
            documentText);

        ArgumentNullException.ThrowIfNull(
            chunkCandidates);

        if (chunkCandidates.Count == 0)
        {
            return Reject(
                "No chunk candidates were provided.");
        }

        var structuralErrors =
            ValidateChunkStructure(
                documentText,
                chunkCandidates);

        if (structuralErrors.Count > 0)
        {
            return BillStatementAiChunkCandidateReconciliationResult
                .Rejected(
                    structuralErrors);
        }

        var validationErrors =
            ValidateChunkCandidates(
                chunkCandidates);

        if (validationErrors.Count > 0)
        {
            return BillStatementAiChunkCandidateReconciliationResult
                .Rejected(
                    validationErrors);
        }

        var mergeErrors =
            new List<string>();

        var providerName =
            MergeString(
                chunkCandidates,
                candidate =>
                    candidate.ProviderName,
                BillStatementAiFactKeys.ProviderName,
                mergeErrors);

        var accountIdentifierSuffix =
            MergeString(
                chunkCandidates,
                candidate =>
                    candidate.AccountIdentifierSuffix,
                BillStatementAiFactKeys.AccountIdentifierSuffix,
                mergeErrors);

        var billingPeriodStart =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.BillingPeriodStart,
                BillStatementAiFactKeys.BillingPeriodStart,
                mergeErrors);

        var billingPeriodEnd =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.BillingPeriodEnd,
                BillStatementAiFactKeys.BillingPeriodEnd,
                mergeErrors);

        var statementDate =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.StatementDate,
                BillStatementAiFactKeys.StatementDate,
                mergeErrors);

        var dueDate =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.DueDate,
                BillStatementAiFactKeys.DueDate,
                mergeErrors);

        var previousBalance =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.PreviousBalance,
                BillStatementAiFactKeys.PreviousBalance,
                mergeErrors);

        var payments =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.Payments,
                BillStatementAiFactKeys.Payments,
                mergeErrors);

        var currentCharges =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.CurrentCharges,
                BillStatementAiFactKeys.CurrentCharges,
                mergeErrors);

        var totalDue =
            MergeValue(
                chunkCandidates,
                candidate =>
                    candidate.TotalDue,
                BillStatementAiFactKeys.TotalDue,
                mergeErrors);

        var currencyCode =
            MergeString(
                chunkCandidates,
                candidate =>
                    candidate.CurrencyCode,
                BillStatementAiFactKeys.CurrencyCode,
                mergeErrors);

        var planOrService =
            MergeString(
                chunkCandidates,
                candidate =>
                    candidate.PlanOrService,
                BillStatementAiFactKeys.PlanOrService,
                mergeErrors);

        var usageSummary =
            MergeString(
                chunkCandidates,
                candidate =>
                    candidate.UsageSummary,
                BillStatementAiFactKeys.UsageSummary,
                mergeErrors);

        var resolvedLineItems =
            ResolveLineItems(
                chunkCandidates,
                mergeErrors);

        if (mergeErrors.Count > 0)
        {
            return BillStatementAiChunkCandidateReconciliationResult
                .Rejected(
                    mergeErrors);
        }

        var lineItems =
            resolvedLineItems
                .Select(
                    item =>
                        item.LineItem)
                .ToList()
                .AsReadOnly();

        var evidence =
            BuildMergedEvidence(
                chunkCandidates,
                resolvedLineItems);

        var mergedCandidate =
            new BillStatementAiCandidate(
                ProviderName:
                    providerName,

                AccountIdentifierSuffix:
                    accountIdentifierSuffix,

                BillingPeriodStart:
                    billingPeriodStart,

                BillingPeriodEnd:
                    billingPeriodEnd,

                StatementDate:
                    statementDate,

                DueDate:
                    dueDate,

                PreviousBalance:
                    previousBalance,

                Payments:
                    payments,

                CurrentCharges:
                    currentCharges,

                TotalDue:
                    totalDue,

                CurrencyCode:
                    currencyCode,

                PlanOrService:
                    planOrService,

                UsageSummary:
                    usageSummary,

                LineItems:
                    lineItems,

                Evidence:
                    evidence,

                /*
                 * Model confidence is deliberately discarded during
                 * deterministic reconciliation. Downstream confidence is based
                 * on evidence/completeness, not a model's self-rating.
                 */
                ModelConfidence:
                    BillStatementAiModelConfidence.Unknown);

        var mergedValidation =
            _candidateValidator.Validate(
                documentText,
                mergedCandidate);

        if (!mergedValidation.IsValid)
        {
            return BillStatementAiChunkCandidateReconciliationResult
                .Rejected(
                    mergedValidation.Errors);
        }

        return BillStatementAiChunkCandidateReconciliationResult
            .Accepted(
                mergedCandidate);
    }

    private static IReadOnlyList<string> ValidateChunkStructure(
        string documentText,
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates)
    {
        var errors =
            new List<string>();

        var expectedStartOffset =
            0;

        for (var index = 0;
             index < chunkCandidates.Count;
             index++)
        {
            var item =
                chunkCandidates[index];

            if (item is null ||
                item.Chunk is null ||
                item.Candidate is null ||
                item.Chunk.Text is null)
            {
                errors.Add(
                    $"Chunk {index} is missing source or candidate data.");

                continue;
            }

            if (item.Chunk.Length == 0)
            {
                errors.Add(
                    $"Chunk {index} has empty source text.");

                continue;
            }

            if (item.Chunk.Index !=
                index)
            {
                errors.Add(
                    $"Chunk {index} has an unexpected index.");

                continue;
            }

            if (item.Chunk.StartOffset !=
                expectedStartOffset)
            {
                errors.Add(
                    $"Chunk {index} does not start at the expected source offset.");

                continue;
            }

            if (item.Chunk.EndOffset >
                documentText.Length)
            {
                errors.Add(
                    $"Chunk {index} extends beyond the source document.");

                continue;
            }

            var expectedText =
                documentText.Substring(
                    item.Chunk.StartOffset,
                    item.Chunk.Length);

            if (!string.Equals(
                    expectedText,
                    item.Chunk.Text,
                    StringComparison.Ordinal))
            {
                errors.Add(
                    $"Chunk {index} does not match the source document.");
            }

            expectedStartOffset =
                item.Chunk.EndOffset;
        }

        if (errors.Count == 0 &&
            expectedStartOffset !=
            documentText.Length)
        {
            errors.Add(
                "Chunk candidates do not cover the complete source document.");
        }

        return errors.AsReadOnly();
    }

    private IReadOnlyList<string> ValidateChunkCandidates(
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates)
    {
        var errors =
            new List<string>();

        foreach (var item in
                 chunkCandidates)
        {
            var validation =
                _candidateValidator.Validate(
                    item.Chunk.Text,
                    item.Candidate);

            foreach (var error in
                     validation.Errors)
            {
                errors.Add(
                    $"Chunk {item.Chunk.Index}: {error}");
            }
        }

        return errors.AsReadOnly();
    }

    private static IReadOnlyList<ResolvedLineItem> ResolveLineItems(
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates,
        ICollection<string> errors)
    {
        var resolved =
            new List<ResolvedLineItem>();

        foreach (var chunkCandidate in
                 chunkCandidates)
        {
            var candidate =
                chunkCandidate.Candidate;

            for (var localIndex = 0;
                 localIndex < candidate.LineItems.Count;
                 localIndex++)
            {
                var lineItem =
                    candidate.LineItems[localIndex];

                var descriptionKey =
                    BillStatementAiFactKeys
                        .LineItemDescription(
                            localIndex);

                var amountKey =
                    BillStatementAiFactKeys
                        .LineItemAmount(
                            localIndex);

                var descriptionEvidence =
                    FindFactEvidence(
                        candidate,
                        descriptionKey);

                var amountEvidence =
                    FindFactEvidence(
                        candidate,
                        amountKey);

                var descriptionRanges =
                    ResolveUniqueEvidenceRanges(
                        chunkCandidate.Chunk,
                        localIndex,
                        descriptionKey,
                        descriptionEvidence,
                        errors);

                var amountRanges =
                    ResolveUniqueEvidenceRanges(
                        chunkCandidate.Chunk,
                        localIndex,
                        amountKey,
                        amountEvidence,
                        errors);

                if (descriptionRanges.Count == 0 ||
                    amountRanges.Count == 0)
                {
                    continue;
                }

                var anchors =
                    (
                        from descriptionRange in descriptionRanges
                        from amountRange in amountRanges
                        where Overlaps(
                            descriptionRange,
                            amountRange)
                        select new SourceRange(
                            Start:
                                Math.Min(
                                    descriptionRange.Start,
                                    amountRange.Start),
                            End:
                                Math.Max(
                                    descriptionRange.End,
                                    amountRange.End))
                    )
                    .Distinct()
                    .ToArray();

                if (anchors.Length !=
                    1)
                {
                    errors.Add(
                        $"Chunk {chunkCandidate.Chunk.Index} line item {localIndex} does not have one unambiguous shared source region for description and amount evidence.");

                    continue;
                }

                resolved.Add(
                    new ResolvedLineItem(
                        ChunkIndex:
                            chunkCandidate.Chunk.Index,
                        LocalIndex:
                            localIndex,
                        Anchor:
                            anchors[0],
                        LineItem:
                            lineItem,
                        DescriptionEvidence:
                            descriptionEvidence,
                        AmountEvidence:
                            amountEvidence));
            }
        }

        var ordered =
            resolved
                .OrderBy(
                    item =>
                        item.ChunkIndex)
                .ThenBy(
                    item =>
                        item.Anchor.Start)
                .ThenBy(
                    item =>
                        item.Anchor.End)
                .ThenBy(
                    item =>
                        item.LocalIndex)
                .ToArray();

        for (var index = 1;
             index < ordered.Length;
             index++)
        {
            var previous =
                ordered[index - 1];

            var current =
                ordered[index];

            if (previous.ChunkIndex !=
                current.ChunkIndex)
            {
                continue;
            }

            if (!Overlaps(
                    previous.Anchor,
                    current.Anchor))
            {
                continue;
            }

            errors.Add(
                $"Chunk {current.ChunkIndex} maps multiple line items to overlapping source evidence.");

            break;
        }

        return ordered;
    }

    private static IReadOnlyList<BillStatementAiEvidence> FindFactEvidence(
        BillStatementAiCandidate candidate,
        string factKey)
    {
        return candidate.Evidence
            .Where(
                evidence =>
                    string.Equals(
                        evidence.FactKey?.Trim(),
                        factKey,
                        StringComparison.Ordinal))
            .ToArray();
    }

    private static IReadOnlyList<SourceRange> ResolveUniqueEvidenceRanges(
        BillStatementAiDocumentChunk chunk,
        int lineItemIndex,
        string factKey,
        IReadOnlyList<BillStatementAiEvidence> evidenceItems,
        ICollection<string> errors)
    {
        var normalizedChunk =
            NormalizeEvidenceText(
                chunk.Text);

        var ranges =
            new List<SourceRange>();

        foreach (var evidence in
                 evidenceItems)
        {
            var normalizedExcerpt =
                NormalizeEvidenceText(
                    evidence.SourceExcerpt);

            var occurrences =
                FindOccurrences(
                    normalizedChunk,
                    normalizedExcerpt);

            if (occurrences.Count !=
                1)
            {
                continue;
            }

            ranges.Add(
                new SourceRange(
                    Start:
                        occurrences[0],
                    End:
                        occurrences[0] +
                        normalizedExcerpt.Length));
        }

        var uniqueRanges =
            ranges
                .Distinct()
                .ToArray();

        if (uniqueRanges.Length ==
            0)
        {
            errors.Add(
                $"Chunk {chunk.Index} line item {lineItemIndex} evidence for '{factKey}' does not identify a unique source occurrence.");
        }

        return uniqueRanges;
    }

    private static IReadOnlyList<int> FindOccurrences(
        string source,
        string value)
    {
        if (string.IsNullOrEmpty(
                value))
        {
            return [];
        }

        var occurrences =
            new List<int>();

        var searchStart =
            0;

        while (searchStart <=
               source.Length -
               value.Length)
        {
            var index =
                source.IndexOf(
                    value,
                    searchStart,
                    StringComparison.OrdinalIgnoreCase);

            if (index <
                0)
            {
                break;
            }

            occurrences.Add(
                index);

            searchStart =
                index +
                1;
        }

        return occurrences;
    }

    private static IReadOnlyList<BillStatementAiEvidence> BuildMergedEvidence(
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates,
        IReadOnlyList<ResolvedLineItem> resolvedLineItems)
    {
        var evidence =
            chunkCandidates
                .SelectMany(
                    item =>
                        item.Candidate.Evidence)
                .Where(
                    item =>
                        !IsLineItemFactKey(
                            item.FactKey))
                .ToList();

        for (var globalIndex = 0;
             globalIndex < resolvedLineItems.Count;
             globalIndex++)
        {
            var resolved =
                resolvedLineItems[globalIndex];

            foreach (var item in
                     resolved.DescriptionEvidence)
            {
                evidence.Add(
                    item with
                    {
                        FactKey =
                            BillStatementAiFactKeys
                                .LineItemDescription(
                                    globalIndex)
                    });
            }

            foreach (var item in
                     resolved.AmountEvidence)
            {
                evidence.Add(
                    item with
                    {
                        FactKey =
                            BillStatementAiFactKeys
                                .LineItemAmount(
                                    globalIndex)
                    });
            }
        }

        return evidence
            .DistinctBy(
                item =>
                    (
                        FactKey:
                            item.FactKey.Trim(),

                        SourceExcerpt:
                            item.SourceExcerpt.Trim(),

                        item.PageNumber))
            .ToList()
            .AsReadOnly();
    }

    private static bool IsLineItemFactKey(
        string? factKey)
    {
        var value =
            factKey?
                .Trim();

        if (string.IsNullOrEmpty(
                value))
        {
            return false;
        }

        if (!value.StartsWith(
                "lineItems[",
                StringComparison.Ordinal))
        {
            return false;
        }

        return value.EndsWith(
                ".description",
                StringComparison.Ordinal) ||
            value.EndsWith(
                ".amount",
                StringComparison.Ordinal);
    }

    private static string NormalizeEvidenceText(
        string value)
    {
        return Regex.Replace(
                value,
                @"\s+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim();
    }

    private static bool Overlaps(
        SourceRange left,
        SourceRange right)
    {
        return left.Start <
                right.End &&
            right.Start <
                left.End;
    }

    private static string? MergeString(
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates,
        Func<BillStatementAiCandidate, string?> selector,
        string factKey,
        ICollection<string> errors)
    {
        var values =
            chunkCandidates
                .Select(
                    item =>
                        selector(
                            item.Candidate))
                .Where(
                    value =>
                        !string.IsNullOrWhiteSpace(
                            value))
                .Select(
                    value =>
                        value!.Trim())
                .Distinct(
                    StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (values.Length <=
            1)
        {
            return values.SingleOrDefault();
        }

        errors.Add(
            $"Chunk candidates conflict on '{factKey}'.");

        return null;
    }

    private static T? MergeValue<T>(
        IReadOnlyList<BillStatementAiChunkCandidate> chunkCandidates,
        Func<BillStatementAiCandidate, T?> selector,
        string factKey,
        ICollection<string> errors)
        where T : struct
    {
        var values =
            chunkCandidates
                .Select(
                    item =>
                        selector(
                            item.Candidate))
                .Where(
                    value =>
                        value.HasValue)
                .Select(
                    value =>
                        value!.Value)
                .Distinct()
                .ToArray();

        if (values.Length <=
            1)
        {
            return values.Length ==
                    0
                ? null
                : values[0];
        }

        errors.Add(
            $"Chunk candidates conflict on '{factKey}'.");

        return null;
    }

    private static BillStatementAiChunkCandidateReconciliationResult Reject(
        string error)
    {
        return BillStatementAiChunkCandidateReconciliationResult
            .Rejected(
                [error]);
    }

    private readonly record struct SourceRange(
        int Start,
        int End);

    private sealed record ResolvedLineItem(
        int ChunkIndex,
        int LocalIndex,
        SourceRange Anchor,
        BillStatementAiLineItemCandidate LineItem,
        IReadOnlyList<BillStatementAiEvidence> DescriptionEvidence,
        IReadOnlyList<BillStatementAiEvidence> AmountEvidence);
}

public sealed record BillStatementAiChunkCandidate(
    BillStatementAiDocumentChunk Chunk,
    BillStatementAiCandidate Candidate);

public sealed record BillStatementAiChunkCandidateReconciliationResult(
    bool IsAccepted,
    BillStatementAiCandidate? Candidate,
    IReadOnlyList<string> Errors)
{
    public static BillStatementAiChunkCandidateReconciliationResult Accepted(
        BillStatementAiCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);

        return new BillStatementAiChunkCandidateReconciliationResult(
            IsAccepted:
                true,

            Candidate:
                candidate,

            Errors:
                []);
    }

    public static BillStatementAiChunkCandidateReconciliationResult Rejected(
        IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(
            errors);

        return new BillStatementAiChunkCandidateReconciliationResult(
            IsAccepted:
                false,

            Candidate:
                null,

            Errors:
                errors);
    }
}

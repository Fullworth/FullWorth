namespace FullWorth.API.Services.Statements;

/// <summary>
/// Deterministically reconciles already-bounded chunk candidates.
///
/// V1 intentionally supports scalar facts only. Line-item reconciliation is
/// rejected until FullWorth has a separate deterministic identity/deduplication
/// design for repeated and cross-chunk line items.
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

        if (chunkCandidates.Any(
                item =>
                    item.Candidate.LineItems.Count >
                    0))
        {
            return Reject(
                "Chunked line-item reconciliation is not supported.");
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

        if (mergeErrors.Count > 0)
        {
            return BillStatementAiChunkCandidateReconciliationResult
                .Rejected(
                    mergeErrors);
        }

        var evidence =
            chunkCandidates
                .SelectMany(
                    item =>
                        item.Candidate.Evidence)
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
                    [],

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

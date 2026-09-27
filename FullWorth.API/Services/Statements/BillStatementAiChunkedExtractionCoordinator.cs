namespace FullWorth.API.Services.Statements;

/// <summary>
/// Development/evaluation coordinator for bounded multi-chunk statement
/// extraction.
///
/// It is intentionally not registered as the production
/// IBillStatementAiExtractor. It performs model calls sequentially, preserves
/// the original request hints/prompt version, and returns a multi-chunk result
/// only when deterministic reconciliation succeeds.
/// </summary>
public sealed class BillStatementAiChunkedExtractionCoordinator
{
    private readonly IBillStatementAiExtractor
        _extractor;

    private readonly BillStatementAiDocumentChunker
        _chunker;

    private readonly BillStatementAiChunkCandidateReconciler
        _reconciler;

    public BillStatementAiChunkedExtractionCoordinator(
        IBillStatementAiExtractor extractor,
        BillStatementAiDocumentChunker chunker,
        BillStatementAiChunkCandidateReconciler reconciler)
    {
        _extractor =
            extractor ??
            throw new ArgumentNullException(
                nameof(extractor));

        _chunker =
            chunker ??
            throw new ArgumentNullException(
                nameof(chunker));

        _reconciler =
            reconciler ??
            throw new ArgumentNullException(
                nameof(reconciler));
    }

    public async Task<BillStatementAiChunkedExtractionResult> ExtractAsync(
        BillStatementAiExtractionRequest request,
        int maxCharactersPerChunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        if (maxCharactersPerChunk <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharactersPerChunk),
                maxCharactersPerChunk,
                "Maximum chunk size must be positive.");
        }

        if (request.DocumentText.Length <=
            maxCharactersPerChunk)
        {
            var candidate =
                await _extractor.ExtractAsync(
                    request,
                    cancellationToken);

            return BillStatementAiChunkedExtractionResult.Accepted(
                candidate,
                usedChunking:
                    false,
                chunkCount:
                    1);
        }

        var chunks =
            _chunker.Plan(
                request.DocumentText,
                maxCharactersPerChunk);

        var chunkCandidates =
            new List<BillStatementAiChunkCandidate>(
                chunks.Count);

        foreach (var chunk in
                 chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidate =
                await _extractor.ExtractAsync(
                    request with
                    {
                        DocumentText =
                            chunk.Text
                    },
                    cancellationToken);

            chunkCandidates.Add(
                new BillStatementAiChunkCandidate(
                    chunk,
                    candidate));
        }

        var reconciliation =
            _reconciler.Reconcile(
                request.DocumentText,
                chunkCandidates);

        if (!reconciliation.IsAccepted ||
            reconciliation.Candidate is null)
        {
            return BillStatementAiChunkedExtractionResult.Rejected(
                chunkCount:
                    chunks.Count,
                errors:
                    reconciliation.Errors);
        }

        return BillStatementAiChunkedExtractionResult.Accepted(
            reconciliation.Candidate,
            usedChunking:
                true,
            chunkCount:
                chunks.Count);
    }
}

public sealed record BillStatementAiChunkedExtractionResult(
    bool IsAccepted,
    BillStatementAiCandidate? Candidate,
    bool UsedChunking,
    int ChunkCount,
    IReadOnlyList<string> Errors)
{
    public static BillStatementAiChunkedExtractionResult Accepted(
        BillStatementAiCandidate candidate,
        bool usedChunking,
        int chunkCount)
    {
        ArgumentNullException.ThrowIfNull(
            candidate);

        return new BillStatementAiChunkedExtractionResult(
            IsAccepted:
                true,
            Candidate:
                candidate,
            UsedChunking:
                usedChunking,
            ChunkCount:
                chunkCount,
            Errors:
                []);
    }

    public static BillStatementAiChunkedExtractionResult Rejected(
        int chunkCount,
        IReadOnlyList<string> errors)
    {
        ArgumentNullException.ThrowIfNull(
            errors);

        return new BillStatementAiChunkedExtractionResult(
            IsAccepted:
                false,
            Candidate:
                null,
            UsedChunking:
                true,
            ChunkCount:
                chunkCount,
            Errors:
                errors);
    }
}

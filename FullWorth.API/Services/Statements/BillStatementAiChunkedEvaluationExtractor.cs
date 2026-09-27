namespace FullWorth.API.Services.Statements;

/*
 * Offline/private-corpus adapter only.
 *
 * This composes the local extractor with deterministic lossless chunk planning
 * and reconciliation without changing the production IBillStatementAiExtractor
 * registration.
 */
public sealed class BillStatementAiChunkedEvaluationExtractor
    : IBillStatementAiExtractor,
      IBillStatementAiInferenceCallCounter
{
    private readonly BillStatementAiChunkedExtractionCoordinator
        _coordinator;

    private readonly IBillStatementAiInferenceCallCounter
        _inferenceCallCounter;

    private readonly int
        _maxCharactersPerChunk;

    public BillStatementAiChunkedEvaluationExtractor(
        IBillStatementAiExtractor extractor,
        int maxCharactersPerChunk)
    {
        ArgumentNullException.ThrowIfNull(
            extractor);

        if (extractor is not
            IBillStatementAiInferenceCallCounter inferenceCallCounter)
        {
            throw new ArgumentException(
                "Chunked offline evaluation requires aggregate inference-call metering.",
                nameof(extractor));
        }

        if (maxCharactersPerChunk <=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxCharactersPerChunk),
                maxCharactersPerChunk,
                "Maximum chunk size must be positive.");
        }

        _inferenceCallCounter =
            inferenceCallCounter;

        _maxCharactersPerChunk =
            maxCharactersPerChunk;

        _coordinator =
            new BillStatementAiChunkedExtractionCoordinator(
                extractor,
                new BillStatementAiDocumentChunker(),
                new BillStatementAiChunkCandidateReconciler(
                    new BillStatementAiCandidateValidator()));
    }

    public long InferenceCallCount =>
        _inferenceCallCounter
            .InferenceCallCount;

    public async Task<BillStatementAiCandidate> ExtractAsync(
        BillStatementAiExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            request);

        var result =
            await _coordinator.ExtractAsync(
                request,
                _maxCharactersPerChunk,
                cancellationToken);

        if (!result.IsAccepted ||
            result.Candidate is null)
        {
            /*
             * Do not include reconciliation errors here. They are derived from
             * untrusted model output and private statement evidence. The
             * offline evaluator treats this fixed signal like any other
             * deterministic candidate rejection: missed truth, not a provider
             * transport failure.
             */
            throw new BillStatementAiDeterministicCandidateRejectionException();
        }

        return result.Candidate;
    }
}

/*
 * Fixed, sanitized control-flow signal for offline evaluation.
 *
 * This is deliberately not a BillStatementAiExtractionException because the
 * provider did respond; FullWorth's deterministic trust boundary rejected the
 * combined candidate.
 */
public sealed class BillStatementAiDeterministicCandidateRejectionException
    : Exception
{
    public BillStatementAiDeterministicCandidateRejectionException()
        : base(
            "Deterministic statement candidate reconciliation rejected the local AI result.")
    {
    }
}

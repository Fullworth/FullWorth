using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiChunkedEvaluationExtractorTests
{
    [Fact]
    public async Task ExtractAsync_OversizedDocument_UsesMultipleCallsAndReconciles()
    {
        const string firstText =
            "ACME Internet\n";

        const string secondText =
            "Total due $42.10 USD\n";

        var inner =
            new CountingExtractor(
                request =>
                    request.DocumentText.Contains(
                        "ACME Internet",
                        StringComparison.Ordinal)
                        ? Candidate(
                            providerName:
                                "ACME Internet",
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.ProviderName,
                                    "ACME Internet")
                            ])
                        : Candidate(
                            totalDue:
                                42.10m,
                            currencyCode:
                                "USD",
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.TotalDue,
                                    "Total due $42.10 USD"),
                                Evidence(
                                    BillStatementAiFactKeys.CurrencyCode,
                                    "Total due $42.10 USD")
                            ]));

        var extractor =
            new BillStatementAiChunkedEvaluationExtractor(
                inner,
                Math.Max(
                    firstText.Length,
                    secondText.Length));

        var candidate =
            await extractor.ExtractAsync(
                Request(
                    firstText +
                    secondText));

        Assert.Equal(
            "ACME Internet",
            candidate.ProviderName);

        Assert.Equal(
            42.10m,
            candidate.TotalDue);

        Assert.Equal(
            2L,
            extractor.InferenceCallCount);

        Assert.Equal(
            2,
            inner.Requests.Count);
    }

    [Fact]
    public async Task ExtractAsync_ConflictingChunks_ThrowsSanitizedDeterministicRejection()
    {
        const string firstText =
            "Total due $42.10\n";

        const string secondText =
            "Total due $52.10\n";

        var inner =
            new CountingExtractor(
                request =>
                    request.DocumentText.Contains(
                        "42.10",
                        StringComparison.Ordinal)
                        ? Candidate(
                            totalDue:
                                42.10m,
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.TotalDue,
                                    "Total due $42.10")
                            ])
                        : Candidate(
                            totalDue:
                                52.10m,
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.TotalDue,
                                    "Total due $52.10")
                            ]));

        var extractor =
            new BillStatementAiChunkedEvaluationExtractor(
                inner,
                firstText.Length);

        var exception =
            await Assert.ThrowsAsync<
                BillStatementAiDeterministicCandidateRejectionException>(
                () =>
                    extractor.ExtractAsync(
                        Request(
                            firstText +
                            secondText)));

        Assert.Equal(
            2L,
            extractor.InferenceCallCount);

        Assert.DoesNotContain(
            "42.10",
            exception.ToString(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "52.10",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_UnmeteredExtractor_FailsClosed()
    {
        var exception =
            Assert.Throws<ArgumentException>(
                () =>
                    new BillStatementAiChunkedEvaluationExtractor(
                        new UnmeteredExtractor(),
                        1_000));

        Assert.Contains(
            "metering",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static BillStatementAiExtractionRequest Request(
        string documentText)
    {
        return new BillStatementAiExtractionRequest(
            DocumentText:
                documentText,
            Hints:
                new BillStatementExtractionHints(
                    ExpectedProviderName:
                        null,
                    ExpectedCategory:
                        null),
            PromptVersion:
                "bill-statement-extraction-v1");
    }

    private static BillStatementAiCandidate Candidate(
        string? providerName = null,
        decimal? totalDue = null,
        string? currencyCode = null,
        IReadOnlyList<BillStatementAiEvidence>? evidence = null)
    {
        return new BillStatementAiCandidate(
            ProviderName:
                providerName,
            AccountIdentifierSuffix:
                null,
            BillingPeriodStart:
                null,
            BillingPeriodEnd:
                null,
            StatementDate:
                null,
            DueDate:
                null,
            PreviousBalance:
                null,
            Payments:
                null,
            CurrentCharges:
                null,
            TotalDue:
                totalDue,
            CurrencyCode:
                currencyCode,
            PlanOrService:
                null,
            UsageSummary:
                null,
            LineItems:
                [],
            Evidence:
                evidence ??
                [],
            ModelConfidence:
                BillStatementAiModelConfidence.Unknown);
    }

    private static BillStatementAiEvidence Evidence(
        string factKey,
        string excerpt)
    {
        return new BillStatementAiEvidence(
            FactKey:
                factKey,
            SourceExcerpt:
                excerpt);
    }

    private sealed class CountingExtractor(
        Func<BillStatementAiExtractionRequest, BillStatementAiCandidate> handler)
        : IBillStatementAiExtractor,
          IBillStatementAiInferenceCallCounter
    {
        public List<BillStatementAiExtractionRequest> Requests
        {
            get;
        } =
            [];

        public long InferenceCallCount
        {
            get;
            private set;
        }

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(
                request);

            InferenceCallCount =
                checked(
                    InferenceCallCount +
                    1);

            return Task.FromResult(
                handler(
                    request));
        }
    }

    private sealed class UnmeteredExtractor
        : IBillStatementAiExtractor
    {
        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}

using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiChunkedExtractionCoordinatorTests
{
    [Fact]
    public async Task ExtractAsync_ShortDocument_UsesSingleModelCall()
    {
        const string documentText =
            "Total due $42.10 USD\n";

        var extractor =
            new RecordingExtractor(
                request =>
                    Candidate(
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

        var result =
            await CreateService(
                    extractor)
                .ExtractAsync(
                    Request(
                        documentText),
                    maxCharactersPerChunk:
                        1_000);

        Assert.True(
            result.IsAccepted);

        Assert.False(
            result.UsedChunking);

        Assert.Equal(
            1,
            result.ChunkCount);

        var call =
            Assert.Single(
                extractor.Requests);

        Assert.Equal(
            documentText,
            call.DocumentText);
    }

    [Fact]
    public async Task ExtractAsync_OversizedDocument_UsesSequentialChunksAndReconciles()
    {
        const string firstText =
            "ACME Internet\nStatement date Sept 1 2026\n";

        const string secondText =
            "Total due $42.10 USD\nDue date Sept 15 2026\n";

        string documentText =
            firstText +
            secondText;

        var extractor =
            new RecordingExtractor(
                request =>
                    request.DocumentText.Contains(
                        "ACME Internet",
                        StringComparison.Ordinal)
                        ? Candidate(
                            providerName:
                                "ACME Internet",
                            statementDate:
                                new DateOnly(
                                    2026,
                                    9,
                                    1),
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.ProviderName,
                                    "ACME Internet"),
                                Evidence(
                                    BillStatementAiFactKeys.StatementDate,
                                    "Statement date Sept 1 2026")
                            ])
                        : Candidate(
                            dueDate:
                                new DateOnly(
                                    2026,
                                    9,
                                    15),
                            totalDue:
                                42.10m,
                            currencyCode:
                                "USD",
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.DueDate,
                                    "Due date Sept 15 2026"),
                                Evidence(
                                    BillStatementAiFactKeys.TotalDue,
                                    "Total due $42.10 USD"),
                                Evidence(
                                    BillStatementAiFactKeys.CurrencyCode,
                                    "Total due $42.10 USD")
                            ]));

        var result =
            await CreateService(
                    extractor)
                .ExtractAsync(
                    Request(
                        documentText),
                    maxCharactersPerChunk:
                        firstText.Length);

        Assert.True(
            result.IsAccepted);

        Assert.True(
            result.UsedChunking);

        Assert.Equal(
            2,
            result.ChunkCount);

        Assert.Equal(
            2,
            extractor.Requests.Count);

        Assert.Equal(
            firstText,
            extractor.Requests[0]
                .DocumentText);

        Assert.Equal(
            secondText,
            extractor.Requests[1]
                .DocumentText);

        Assert.All(
            extractor.Requests,
            request =>
            {
                Assert.Equal(
                    "bill-statement-extraction-v1",
                    request.PromptVersion);

                Assert.Equal(
                    "ACME",
                    request.Hints.ExpectedProviderName);
            });

        Assert.Equal(
            "ACME Internet",
            result.Candidate?.ProviderName);

        Assert.Equal(
            42.10m,
            result.Candidate?.TotalDue);
    }

    [Fact]
    public async Task ExtractAsync_ConflictingChunkFacts_ReturnsRejectedResult()
    {
        const string firstText =
            "Total due $42.10\n";

        const string secondText =
            "Total due $52.10\n";

        var extractor =
            new RecordingExtractor(
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

        var result =
            await CreateService(
                    extractor)
                .ExtractAsync(
                    Request(
                        firstText +
                        secondText),
                    maxCharactersPerChunk:
                        firstText.Length);

        Assert.False(
            result.IsAccepted);

        Assert.True(
            result.UsedChunking);

        Assert.Null(
            result.Candidate);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    BillStatementAiFactKeys.TotalDue,
                    StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExtractAsync_LineItemsAcrossChunks_FailsClosed()
    {
        const string text =
            "Service fee $5.00\nmore text\n";

        var extractor =
            new RecordingExtractor(
                request =>
                    request.DocumentText.Contains(
                        "Service fee",
                        StringComparison.Ordinal)
                        ? Candidate(
                            lineItems:
                            [
                                new BillStatementAiLineItemCandidate(
                                    Description:
                                        "Service fee",
                                    Amount:
                                        5.00m,
                                    Kind:
                                        BillStatementAiLineItemKind.Fee)
                            ],
                            evidence:
                            [
                                Evidence(
                                    BillStatementAiFactKeys.LineItemDescription(
                                        0),
                                    "Service fee $5.00"),
                                Evidence(
                                    BillStatementAiFactKeys.LineItemAmount(
                                        0),
                                    "Service fee $5.00")
                            ])
                        : Candidate());

        var result =
            await CreateService(
                    extractor)
                .ExtractAsync(
                    Request(
                        text),
                    maxCharactersPerChunk:
                        18);

        Assert.False(
            result.IsAccepted);

        Assert.Contains(
            "Chunked line-item reconciliation is not supported.",
            result.Errors);
    }

    [Fact]
    public async Task ExtractAsync_CancellationBeforeLaterChunk_StopsFurtherCalls()
    {
        const string firstText =
            "first chunk line\n";

        const string secondText =
            "second chunk line\n";

        using var cancellation =
            new CancellationTokenSource();

        var extractor =
            new RecordingExtractor(
                request =>
                {
                    cancellation.Cancel();

                    return Candidate();
                });

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () =>
                CreateService(
                        extractor)
                    .ExtractAsync(
                        Request(
                            firstText +
                            secondText),
                        maxCharactersPerChunk:
                            firstText.Length,
                        cancellation.Token));

        Assert.Single(
            extractor.Requests);
    }

    private static BillStatementAiChunkedExtractionCoordinator CreateService(
        IBillStatementAiExtractor extractor)
    {
        return new BillStatementAiChunkedExtractionCoordinator(
            extractor,
            new BillStatementAiDocumentChunker(),
            new BillStatementAiChunkCandidateReconciler(
                new BillStatementAiCandidateValidator()));
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
                        "ACME",
                    ExpectedCategory:
                        "Internet"),
            PromptVersion:
                "bill-statement-extraction-v1");
    }

    private static BillStatementAiCandidate Candidate(
        string? providerName = null,
        DateOnly? statementDate = null,
        DateOnly? dueDate = null,
        decimal? totalDue = null,
        string? currencyCode = null,
        IReadOnlyList<BillStatementAiLineItemCandidate>? lineItems = null,
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
                statementDate,
            DueDate:
                dueDate,
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
                lineItems ??
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

    private sealed class RecordingExtractor(
        Func<BillStatementAiExtractionRequest, BillStatementAiCandidate> handler)
        : IBillStatementAiExtractor
    {
        public List<BillStatementAiExtractionRequest> Requests
        {
            get;
        } =
            [];

        public Task<BillStatementAiCandidate> ExtractAsync(
            BillStatementAiExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Requests.Add(
                request);

            return Task.FromResult(
                handler(
                    request));
        }
    }
}

using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiChunkCandidateReconcilerTests
{
    [Fact]
    public void Reconcile_ComplementaryScalarFacts_MergesDeterministically()
    {
        const string firstText =
            "ACME Internet\nStatement date Sept 1 2026\n";

        const string secondText =
            "Total due $42.10 USD\nDue date Sept 15 2026\n";

        string documentText =
            firstText +
            secondText;

        var result =
            CreateService()
                .Reconcile(
                    documentText,
                    [
                        Item(
                            index:
                                0,
                            startOffset:
                                0,
                            text:
                                firstText,
                            candidate:
                                Candidate(
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
                                    ],
                                    confidence:
                                        BillStatementAiModelConfidence.High)),

                        Item(
                            index:
                                1,
                            startOffset:
                                firstText.Length,
                            text:
                                secondText,
                            candidate:
                                Candidate(
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
                                    ],
                                    confidence:
                                        BillStatementAiModelConfidence.High))
                    ]);

        Assert.True(
            result.IsAccepted);

        Assert.NotNull(
            result.Candidate);

        Assert.Equal(
            "ACME Internet",
            result.Candidate.ProviderName);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                1),
            result.Candidate.StatementDate);

        Assert.Equal(
            new DateOnly(
                2026,
                9,
                15),
            result.Candidate.DueDate);

        Assert.Equal(
            42.10m,
            result.Candidate.TotalDue);

        Assert.Equal(
            "USD",
            result.Candidate.CurrencyCode);

        Assert.Equal(
            BillStatementAiModelConfidence.Unknown,
            result.Candidate.ModelConfidence);

        Assert.Empty(
            result.Candidate.LineItems);

        Assert.Equal(
            5,
            result.Candidate.Evidence.Count);
    }

    [Fact]
    public void Reconcile_ConflictingTotalDue_RejectsInsteadOfChoosing()
    {
        const string firstText =
            "Total due $42.10\n";

        const string secondText =
            "Total due $52.10\n";

        var result =
            CreateService()
                .Reconcile(
                    firstText +
                    secondText,
                    [
                        Item(
                            0,
                            0,
                            firstText,
                            Candidate(
                                totalDue:
                                    42.10m,
                                evidence:
                                [
                                    Evidence(
                                        BillStatementAiFactKeys.TotalDue,
                                        "Total due $42.10")
                                ])),

                        Item(
                            1,
                            firstText.Length,
                            secondText,
                            Candidate(
                                totalDue:
                                    52.10m,
                                evidence:
                                [
                                    Evidence(
                                        BillStatementAiFactKeys.TotalDue,
                                        "Total due $52.10")
                                ]))
                    ]);

        Assert.False(
            result.IsAccepted);

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
    public void Reconcile_ChunkDoesNotCoverOriginalSource_Rejects()
    {
        const string documentText =
            "first\nsecond\n";

        const string firstText =
            "first\n";

        const string secondText =
            "second\n";

        var result =
            CreateService()
                .Reconcile(
                    documentText,
                    [
                        Item(
                            0,
                            0,
                            firstText,
                            Candidate()),

                        Item(
                            1,
                            firstText.Length + 1,
                            secondText,
                            Candidate())
                    ]);

        Assert.False(
            result.IsAccepted);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "expected source offset",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reconcile_InvalidChunkEvidence_RejectsBeforeMerge()
    {
        const string text =
            "Total due $42.10\n";

        var result =
            CreateService()
                .Reconcile(
                    text,
                    [
                        Item(
                            0,
                            0,
                            text,
                            Candidate(
                                totalDue:
                                    42.10m,
                                evidence:
                                [
                                    Evidence(
                                        BillStatementAiFactKeys.TotalDue,
                                        "Total due $999.99")
                                ]))
                    ]);

        Assert.False(
            result.IsAccepted);

        Assert.Contains(
            result.Errors,
            error =>
                error.StartsWith(
                    "Chunk 0:",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Reconcile_LineItemsAcrossChunks_PreservesSourceOrderAndReindexesEvidence()
    {
        const string firstText =
            "Service fee $5.00\n";

        const string secondText =
            "State tax $1.25\n";

        var result =
            CreateService()
                .Reconcile(
                    firstText +
                    secondText,
                    [
                        Item(
                            0,
                            0,
                            firstText,
                            Candidate(
                                lineItems:
                                [
                                    LineItem(
                                        "Service fee",
                                        5.00m,
                                        BillStatementAiLineItemKind.Fee)
                                ],
                                evidence:
                                LineItemEvidence(
                                    0,
                                    "Service fee $5.00"))),

                        Item(
                            1,
                            firstText.Length,
                            secondText,
                            Candidate(
                                lineItems:
                                [
                                    LineItem(
                                        "State tax",
                                        1.25m,
                                        BillStatementAiLineItemKind.Tax)
                                ],
                                evidence:
                                LineItemEvidence(
                                    0,
                                    "State tax $1.25")))
                    ]);

        Assert.True(
            result.IsAccepted);

        Assert.NotNull(
            result.Candidate);

        Assert.Equal(
            2,
            result.Candidate.LineItems.Count);

        Assert.Equal(
            "Service fee",
            result.Candidate.LineItems[0]
                .Description);

        Assert.Equal(
            "State tax",
            result.Candidate.LineItems[1]
                .Description);

        Assert.Contains(
            result.Candidate.Evidence,
            evidence =>
                evidence.FactKey ==
                    BillStatementAiFactKeys
                        .LineItemDescription(
                            0) &&
                evidence.SourceExcerpt ==
                    "Service fee $5.00");

        Assert.Contains(
            result.Candidate.Evidence,
            evidence =>
                evidence.FactKey ==
                    BillStatementAiFactKeys
                        .LineItemAmount(
                            1) &&
                evidence.SourceExcerpt ==
                    "State tax $1.25");
    }

    [Fact]
    public void Reconcile_IdenticalPhysicalLineItemsInDifferentChunks_PreservesBoth()
    {
        const string line =
            "Service fee $5.00\n";

        var candidate =
            Candidate(
                lineItems:
                [
                    LineItem(
                        "Service fee",
                        5.00m,
                        BillStatementAiLineItemKind.Fee)
                ],
                evidence:
                LineItemEvidence(
                    0,
                    "Service fee $5.00"));

        var result =
            CreateService()
                .Reconcile(
                    line +
                    line,
                    [
                        Item(
                            0,
                            0,
                            line,
                            candidate),

                        Item(
                            1,
                            line.Length,
                            line,
                            candidate)
                    ]);

        Assert.True(
            result.IsAccepted);

        Assert.Equal(
            2,
            result.Candidate?.LineItems.Count);
    }

    [Fact]
    public void Reconcile_RepeatedExcerptInsideOneChunk_RejectsAmbiguousLineItemIdentity()
    {
        const string text =
            "Service fee $5.00\nService fee $5.00\n";

        var result =
            CreateService()
                .Reconcile(
                    text,
                    [
                        Item(
                            0,
                            0,
                            text,
                            Candidate(
                                lineItems:
                                [
                                    LineItem(
                                        "Service fee",
                                        5.00m,
                                        BillStatementAiLineItemKind.Fee)
                                ],
                                evidence:
                                LineItemEvidence(
                                    0,
                                    "Service fee $5.00")))
                    ]);

        Assert.False(
            result.IsAccepted);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "unique source occurrence",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reconcile_MultipleLineItemsMappedToSameSourceEvidence_Rejects()
    {
        const string text =
            "Service fee $5.00\n";

        var evidence =
            new List<BillStatementAiEvidence>();

        evidence.AddRange(
            LineItemEvidence(
                0,
                "Service fee $5.00"));

        evidence.AddRange(
            LineItemEvidence(
                1,
                "Service fee $5.00"));

        var result =
            CreateService()
                .Reconcile(
                    text,
                    [
                        Item(
                            0,
                            0,
                            text,
                            Candidate(
                                lineItems:
                                [
                                    LineItem(
                                        "Service fee",
                                        5.00m,
                                        BillStatementAiLineItemKind.Fee),

                                    LineItem(
                                        "Service fee",
                                        5.00m,
                                        BillStatementAiLineItemKind.Fee)
                                ],
                                evidence:
                                    evidence))
                    ]);

        Assert.False(
            result.IsAccepted);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "overlapping source evidence",
                    StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Reconcile_CaseOnlyStringDifference_DoesNotCreateConflict()
    {
        const string firstText =
            "Currency USD\n";

        const string secondText =
            "Currency usd\n";

        var result =
            CreateService()
                .Reconcile(
                    firstText +
                    secondText,
                    [
                        Item(
                            0,
                            0,
                            firstText,
                            Candidate(
                                currencyCode:
                                    "USD",
                                evidence:
                                [
                                    Evidence(
                                        BillStatementAiFactKeys.CurrencyCode,
                                        "Currency USD")
                                ])),

                        Item(
                            1,
                            firstText.Length,
                            secondText,
                            Candidate(
                                currencyCode:
                                    "usd",
                                evidence:
                                [
                                    Evidence(
                                        BillStatementAiFactKeys.CurrencyCode,
                                        "Currency usd")
                                ]))
                    ]);

        Assert.True(
            result.IsAccepted);

        Assert.Equal(
            "USD",
            result.Candidate?.CurrencyCode);
    }

    private static BillStatementAiChunkCandidateReconciler CreateService()
    {
        return new BillStatementAiChunkCandidateReconciler(
            new BillStatementAiCandidateValidator());
    }

    private static BillStatementAiChunkCandidate Item(
        int index,
        int startOffset,
        string text,
        BillStatementAiCandidate candidate)
    {
        return new BillStatementAiChunkCandidate(
            new BillStatementAiDocumentChunk(
                Index:
                    index,
                StartOffset:
                    startOffset,
                Text:
                    text),
            candidate);
    }

    private static BillStatementAiCandidate Candidate(
        string? providerName = null,
        string? accountIdentifierSuffix = null,
        DateOnly? billingPeriodStart = null,
        DateOnly? billingPeriodEnd = null,
        DateOnly? statementDate = null,
        DateOnly? dueDate = null,
        decimal? previousBalance = null,
        decimal? payments = null,
        decimal? currentCharges = null,
        decimal? totalDue = null,
        string? currencyCode = null,
        string? planOrService = null,
        string? usageSummary = null,
        IReadOnlyList<BillStatementAiLineItemCandidate>? lineItems = null,
        IReadOnlyList<BillStatementAiEvidence>? evidence = null,
        BillStatementAiModelConfidence confidence =
            BillStatementAiModelConfidence.Unknown)
    {
        return new BillStatementAiCandidate(
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
                lineItems ??
                [],
            Evidence:
                evidence ??
                [],
            ModelConfidence:
                confidence);
    }

    private static BillStatementAiLineItemCandidate LineItem(
        string description,
        decimal amount,
        BillStatementAiLineItemKind kind)
    {
        return new BillStatementAiLineItemCandidate(
            Description:
                description,
            Amount:
                amount,
            Kind:
                kind);
    }

    private static IReadOnlyList<BillStatementAiEvidence> LineItemEvidence(
        int index,
        string excerpt)
    {
        return
        [
            Evidence(
                BillStatementAiFactKeys
                    .LineItemDescription(
                        index),
                excerpt),

            Evidence(
                BillStatementAiFactKeys
                    .LineItemAmount(
                        index),
                excerpt)
        ];
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
}

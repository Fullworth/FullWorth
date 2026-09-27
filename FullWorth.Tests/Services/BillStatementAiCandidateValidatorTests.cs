using FullWorth.API.Services.Statements;

namespace FullWorth.Tests.Services;

public sealed class BillStatementAiCandidateValidatorTests
{
    [Fact]
    public void EvidenceBackedCandidate_IsAccepted()
    {
        const string documentText =
            """
            MIDCO
            Account ending 1234
            Billing period August 1, 2026 through August 31, 2026
            Internet service $99.99
            Equipment fee $5.00
            Total due $104.99
            Due September 20, 2026
            USD
            """;

        var candidate =
            new BillStatementAiCandidate(
                ProviderName:
                    "MIDCO",

                AccountIdentifierSuffix:
                    "1234",

                BillingPeriodStart:
                    new DateOnly(
                        2026,
                        8,
                        1),

                BillingPeriodEnd:
                    new DateOnly(
                        2026,
                        8,
                        31),

                StatementDate:
                    null,

                DueDate:
                    new DateOnly(
                        2026,
                        9,
                        20),

                PreviousBalance:
                    null,

                Payments:
                    null,

                CurrentCharges:
                    null,

                TotalDue:
                    104.99m,

                CurrencyCode:
                    "USD",

                PlanOrService:
                    "Internet service",

                UsageSummary:
                    null,

                LineItems:
                    [
                        new BillStatementAiLineItemCandidate(
                            Description:
                                "Internet service",

                            Amount:
                                99.99m,

                            Kind:
                                BillStatementAiLineItemKind.Service),

                        new BillStatementAiLineItemCandidate(
                            Description:
                                "Equipment fee",

                            Amount:
                                5.00m,

                            Kind:
                                BillStatementAiLineItemKind.Fee)
                    ],

                Evidence:
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.ProviderName,
                            "MIDCO"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.AccountIdentifierSuffix,
                            "Account ending 1234"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodStart,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.BillingPeriodEnd,
                            "Billing period August 1, 2026 through August 31, 2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.DueDate,
                            "Due September 20, 2026"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $104.99"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.CurrencyCode,
                            "USD"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.PlanOrService,
                            "Internet service $99.99"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.LineItemDescription(0),
                            "Internet service $99.99"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.LineItemAmount(0),
                            "Internet service $99.99"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.LineItemDescription(1),
                            "Equipment fee $5.00"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.LineItemAmount(1),
                            "Equipment fee $5.00")
                    ],

                ModelConfidence:
                    BillStatementAiModelConfidence.High);

        var validator =
            new BillStatementAiCandidateValidator();

        var result =
            validator.Validate(
                documentText,
                candidate);

        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));

        Assert.Empty(
            result.Errors);
    }

    [Fact]
    public void StringEvidence_RejectsValueThatIsOnlyPartOfAnotherWord()
    {
        const string documentText =
            "MIDCO total due $104.99";

        var candidate =
            CreateEmptyCandidate() with
            {
                ProviderName =
                    "MID",

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.ProviderName,
                            "MIDCO")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "does not contain the extracted value",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void StringEvidence_AcceptsWholeValueInsideLongerExcerpt()
    {
        const string documentText =
            "Internet service $20.00";

        var candidate =
            CreateEmptyCandidate() with
            {
                PlanOrService =
                    "Internet",

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.PlanOrService,
                            "Internet service $20.00")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }

    [Fact]
    public void MoneyEvidence_DoesNotTreatDateYearAsAmount()
    {
        const string documentText =
            "Statement date September 20, 2026";

        var candidate =
            CreateEmptyCandidate() with
            {
                TotalDue =
                    2026m,

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Statement date September 20, 2026")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "does not contain the extracted value",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Due Sep 20 2026")]
    [InlineData("Due Sept 20 2026")]
    [InlineData("Due September 20 2026")]
    public void DateEvidence_AcceptsMonthFirstDateWithoutComma(
        string documentText)
    {
        var candidate =
            CreateEmptyCandidate() with
            {
                DueDate =
                    new DateOnly(
                        2026,
                        9,
                        20),

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.DueDate,
                            documentText)
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }

    [Fact]
    public void DateEvidence_RejectsDateEmbeddedInLongerDigits()
    {
        const string documentText =
            "Due September 20, 20260";

        var candidate =
            CreateEmptyCandidate() with
            {
                DueDate =
                    new DateOnly(
                        2026,
                        9,
                        20),

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.DueDate,
                            "September 20, 20260")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "does not contain the extracted value",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void InventedEvidence_IsRejected()
    {
        const string documentText =
            """
            MIDCO
            Total due $104.99
            """;

        var candidate =
            CreateEmptyCandidate() with
            {
                TotalDue =
                    104.99m,

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $149.99")
                    ]
            };

        var validator =
            new BillStatementAiCandidateValidator();

        var result =
            validator.Validate(
                documentText,
                candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "was not found in the source document",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ExtractedFactWithoutEvidence_IsRejected()
    {
        const string documentText =
            """
            MIDCO
            Total due $104.99
            """;

        var candidate =
            CreateEmptyCandidate() with
            {
                ProviderName =
                    "MIDCO"
            };

        var validator =
            new BillStatementAiCandidateValidator();

        var result =
            validator.Validate(
                documentText,
                candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    BillStatementAiFactKeys.ProviderName,
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RealExcerptThatDoesNotContainClaimedAmount_IsRejected()
    {
        const string documentText =
            """
            MIDCO
            Total due $104.99
            """;

        var candidate =
            CreateEmptyCandidate() with
            {
                TotalDue =
                    999.99m,

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $104.99")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "does not contain the extracted value",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void RealExcerptThatDoesNotContainClaimedLineItemAmount_IsRejected()
    {
        const string documentText =
            """
            Equipment fee $5.00
            """;

        var candidate =
            CreateEmptyCandidate() with
            {
                LineItems =
                    [
                        new BillStatementAiLineItemCandidate(
                            "Equipment fee",
                            50m,
                            BillStatementAiLineItemKind.Fee)
                    ],

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.LineItemDescription(0),
                            "Equipment fee $5.00"),

                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.LineItemAmount(0),
                            "Equipment fee $5.00")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    BillStatementAiFactKeys.LineItemAmount(0),
                    StringComparison.Ordinal) &&
                error.Contains(
                    "does not contain the extracted value",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void AccountSuffixEvidence_AcceptsSuffixOfLongerAccountNumber()
    {
        const string documentText =
            "Account number 1234567890123456";

        var candidate =
            CreateEmptyCandidate() with
            {
                AccountIdentifierSuffix =
                    "3456",

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.AccountIdentifierSuffix,
                            "Account number 1234567890123456")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.True(
            result.IsValid,
            string.Join(
                Environment.NewLine,
                result.Errors));
    }

    [Fact]
    public void FullAccountNumberLikeValue_IsRejected()
    {
        const string documentText =
            """
            Account number 1234567890123456
            """;

        var candidate =
            CreateEmptyCandidate() with
            {
                AccountIdentifierSuffix =
                    "1234567890123456",

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.AccountIdentifierSuffix,
                            "Account number 1234567890123456")
                    ]
            };

        var validator =
            new BillStatementAiCandidateValidator();

        var result =
            validator.Validate(
                documentText,
                candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "suffix is too long",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void DecimalMinimumValue_IsRejectedWithoutThrowing()
    {
        const string documentText =
            """
            Total due $104.99
            """;

        var candidate =
            CreateEmptyCandidate() with
            {
                TotalDue =
                    decimal.MinValue,

                Evidence =
                    [
                        new BillStatementAiEvidence(
                            BillStatementAiFactKeys.TotalDue,
                            "Total due $104.99")
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    documentText,
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "invalid monetary value",
                    StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("", "does not contain a source excerpt")]
    [InlineData("unrelated", "was not found in the source document")]
    public void ModelSuppliedFactKey_IsNeverEchoedInEvidenceErrors(
        string sourceExcerpt,
        string expectedError)
    {
        const string privateFactKey =
            "Private account 1234567890123456";

        var candidate =
            CreateEmptyCandidate() with
            {
                Evidence =
                    [
                        new BillStatementAiEvidence(
                            privateFactKey,
                            sourceExcerpt)
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    "Statement total due $42.00",
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    expectedError,
                    StringComparison.Ordinal));

        Assert.All(
            result.Errors,
            error =>
                Assert.DoesNotContain(
                    privateFactKey,
                    error,
                    StringComparison.Ordinal));
    }

    [Fact]
    public void LongModelSuppliedFactKey_IsNeverEchoedInEvidenceErrors()
    {
        const string privateFactKey =
            "Private account 1234567890123456";

        var candidate =
            CreateEmptyCandidate() with
            {
                Evidence =
                    [
                        new BillStatementAiEvidence(
                            privateFactKey,
                            new string(
                                'x',
                                501))
                    ]
            };

        var result =
            new BillStatementAiCandidateValidator()
                .Validate(
                    "Statement total due $42.00",
                    candidate);

        Assert.False(
            result.IsValid);

        Assert.Contains(
            result.Errors,
            error =>
                error.Contains(
                    "is too long",
                    StringComparison.Ordinal));

        Assert.All(
            result.Errors,
            error =>
                Assert.DoesNotContain(
                    privateFactKey,
                    error,
                    StringComparison.Ordinal));
    }

    private static BillStatementAiCandidate
        CreateEmptyCandidate()
    {
        return new BillStatementAiCandidate(
            ProviderName:
                null,

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
                null,

            CurrencyCode:
                null,

            PlanOrService:
                null,

            UsageSummary:
                null,

            LineItems:
                [],

            Evidence:
                [],

            ModelConfidence:
                BillStatementAiModelConfidence.Unknown);
    }
}

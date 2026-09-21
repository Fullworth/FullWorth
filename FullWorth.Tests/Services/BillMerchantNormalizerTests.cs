using FullWorth.Core.Services;
using Xunit;

namespace FullWorth.Tests.Services;

public sealed class BillMerchantNormalizerTests
{
    private readonly BillMerchantNormalizer
        _normalizer = new();

    [Theory]
    [InlineData("MIDCO", "MIDCO")]
    [InlineData("Midco", "MIDCO")]
    [InlineData("MIDCO AUTOPAY", "MIDCO")]
    [InlineData("MIDCO PAYMENT", "MIDCO")]
    [InlineData("MIDCO ONLINE PAYMENT", "MIDCO")]
    [InlineData("MIDCO*123456", "MIDCO")]
    [InlineData("MIDCO - 9876", "MIDCO")]
    [InlineData("MIDCO AUTOPAYMENT", "MIDCO")]
    [InlineData("MIDCO PMT", "MIDCO")]
    [InlineData("MIDCO RECURRING PURCHASE", "MIDCO")]
    [InlineData("MIDCO CHECKCARD 4829AB", "MIDCO")]
    [InlineData("MIDCO A123456", "MIDCO")]
    public void Normalize_TransactionDescriptionVariants_ReturnSameMerchant(
        string input,
        string expected)
    {
        var result =
            _normalizer.Normalize(input);

        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankValue_ReturnsEmpty(
        string? input)
    {
        var result =
            _normalizer.Normalize(input);

        Assert.Equal(
            string.Empty,
            result);
    }

    [Theory]
    [InlineData("Black Hills Energy", "BLACK HILLS ENERGY")]
    [InlineData("7ELEVEN", "7ELEVEN")]
    [InlineData("Studio 54", "STUDIO 54")]
    public void Normalize_PreservesMeaningfulMerchantWords(
        string input,
        string expected)
    {
        var result =
            _normalizer.Normalize(input);

        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void Normalize_RemovesTransactionNoise()
    {
        var result =
            _normalizer.Normalize(
                "Verizon Wireless ACH AUTOPAY 482913");

        Assert.Equal(
            "VERIZON WIRELESS",
            result);
    }
}

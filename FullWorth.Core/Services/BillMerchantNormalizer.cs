using System.Text;
using System.Text.RegularExpressions;

namespace FullWorth.Core.Services;

public sealed class BillMerchantNormalizer
{
    private static readonly HashSet<string> NoiseWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "ACH",
            "AUTOPAY",
            "AUTOPAYMENT",
            "AUTO",
            "PAY",
            "PAYMENT",
            "PMT",
            "ONLINE",
            "WEB",
            "DEBIT",
            "CARD",
            "CHECKCARD",
            "POS",
            "PURCHASE",
            "RECURRING"
        };

    public string Normalize(string? merchantName)
    {
        if (string.IsNullOrWhiteSpace(merchantName))
        {
            return string.Empty;
        }

        var cleaned =
            ReplacePunctuationWithSpaces(
                merchantName.Trim());

        var parts =
            Regex.Split(
                    cleaned,
                    @"\s+")
                .Where(part =>
                    !string.IsNullOrWhiteSpace(part))
                .Where(part =>
                    !NoiseWords.Contains(part))
                .Where(part =>
                    !IsReferenceToken(part))
                .Select(part =>
                    part.ToUpperInvariant())
                .ToList();

        return string.Join(
            ' ',
            parts);
    }

    private static string ReplacePunctuationWithSpaces(
        string value)
    {
        var builder =
            new StringBuilder(
                value.Length);

        foreach (var character in value)
        {
            builder.Append(
                char.IsLetterOrDigit(character) ||
                character == '&'
                    ? character
                    : ' ');
        }

        return builder.ToString();
    }

    private static bool IsReferenceToken(
        string value)
    {
        if (value.Length < 4)
        {
            return false;
        }

        var digitCount =
            value.Count(char.IsDigit);

        if (digitCount ==
            value.Length)
        {
            return true;
        }

        /*
         * Bank descriptions frequently append authorization/reference IDs
         * such as 4829AB or A123456. Treat a mixed token as a reference only
         * when it contains enough digits to be transaction-specific. This
         * deliberately preserves meaningful merchant tokens such as 7ELEVEN.
         */
        return value.Length >= 6 &&
               digitCount >= 4;
    }
}

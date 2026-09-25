using FullWorth.Core.Models;

namespace FullWorth.Core.Services;

public sealed class BillStreamDiscoveryService
{
    private readonly RecurringBillDetectionService _recurringBillDetectionService;
    private readonly BillMerchantNormalizer _merchantNormalizer;

    public BillStreamDiscoveryService()
    {
        _recurringBillDetectionService =
            new RecurringBillDetectionService();
        _merchantNormalizer =
            new BillMerchantNormalizer();
    }

    public IReadOnlyList<BillStream> Discover(
        IEnumerable<BankTransaction> transactions,
        IEnumerable<BillStatement>? statements = null)
    {
        ArgumentNullException.ThrowIfNull(transactions);

        var transactionList =
            transactions
                .Where(transaction =>
                    !transaction.IsPending)
                .ToList();

        var statementList =
            statements?.ToList()
            ?? [];

        var detectedBills =
            _recurringBillDetectionService.Detect(
                transactionList);

        var streams =
            new List<BillStream>();

        foreach (var detectedBill in detectedBills)
        {
            var matchingTransactions =
                transactionList
                    .Where(transaction =>
                        string.Equals(
                            _merchantNormalizer.Normalize(
                                transaction.MerchantName),
                            _merchantNormalizer.Normalize(
                                detectedBill.MerchantName),
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(transaction =>
                        transaction.PostedDate)
                    .ToList();

            var matchingStatements =
                statementList
                    .Where(statement =>
                        string.Equals(
                            _merchantNormalizer.Normalize(
                                statement.ProviderName),
                            _merchantNormalizer.Normalize(
                                detectedBill.MerchantName),
                            StringComparison.OrdinalIgnoreCase))
                    .OrderBy(statement =>
                        statement.BillingPeriodStart)
                    .ToList();

            var category =
                DetermineCategory(
                    detectedBill.MerchantName);

            streams.Add(
                new BillStream(
                    id: Guid.NewGuid(),
                    providerName: detectedBill.MerchantName,
                    category: category,
                    transactions: matchingTransactions,
                    statements: matchingStatements));
        }

        return streams
            .OrderBy(
                stream => stream.ProviderName,
                StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();
    }

    private static BillCategory DetermineCategory(
        string providerName)
    {
        if (providerName.Contains(
                "Midco",
                StringComparison.OrdinalIgnoreCase))
        {
            return BillCategory.Internet;
        }

        if (providerName.Contains(
                "Verizon",
                StringComparison.OrdinalIgnoreCase))
        {
            return BillCategory.MobilePhone;
        }

        if (providerName.Contains(
                "Black Hills Energy",
                StringComparison.OrdinalIgnoreCase))
        {
            return BillCategory.Utility;
        }

        return BillCategory.Unknown;
    }
}

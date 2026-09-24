using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementEvidenceAlertService
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    public IReadOnlyList<BillAlertDesiredState>
        BuildDesiredAlerts(
            Guid userId,
            Guid billStreamId,
            string providerName,
            BillChangeEntity change,
            IReadOnlyList<BillLineItemEntity> previousLineItems,
            IReadOnlyList<BillLineItemEntity> currentLineItems)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        if (billStreamId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "Bill stream ID is required.",
                nameof(billStreamId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerName);

        ArgumentNullException.ThrowIfNull(
            change);

        ArgumentNullException.ThrowIfNull(
            previousLineItems);

        ArgumentNullException.ThrowIfNull(
            currentLineItems);

        if (change.UserId !=
                userId ||
            change.BillStreamId !=
                billStreamId)
        {
            throw new InvalidOperationException(
                "The bill change does not belong to the requested user and bill stream.");
        }

        ValidateLineItemOwnership(
            userId,
            change.PreviousStatementId,
            previousLineItems);

        ValidateLineItemOwnership(
            userId,
            change.CurrentStatementId,
            currentLineItems);

        return BuildDesiredAlertsCore(
            providerName.Trim(),
            previousLineItems,
            currentLineItems);
    }

    private static IReadOnlyList<BillAlertDesiredState>
        BuildDesiredAlertsCore(
            string providerName,
            IReadOnlyList<BillLineItemEntity> previousLineItems,
            IReadOnlyList<BillLineItemEntity> currentLineItems)
    {
        var previous =
            Aggregate(
                previousLineItems);

        var current =
            Aggregate(
                currentLineItems);

        var results =
            new List<BillAlertDesiredState>();

        foreach (var currentItem in
                 current.Values
                     .Where(
                         item =>
                             string.Equals(
                                 item.Category,
                                 "Fee",
                                 StringComparison.OrdinalIgnoreCase) &&
                             item.Amount >
                                 0m)
                     .OrderBy(
                         item =>
                             item.Description,
                         StringComparer.OrdinalIgnoreCase))
        {
            previous.TryGetValue(
                currentItem.Description,
                out var previousItem);

            var previousAmount =
                previousItem?.Amount
                ?? 0m;

            if (previousAmount >
                0m)
            {
                continue;
            }

            var title =
                Truncate(
                    $"{providerName}: new fee — {currentItem.Description}",
                    MaxTitleLength);

            var message =
                Truncate(
                    $"{FormatMoney(currentItem.Amount)} labeled \"{currentItem.Description}\" appeared on the latest provider statement. FullWorth is not assuming this fee will recur.",
                    MaxMessageLength);

            results.Add(
                new BillAlertDesiredState(
                    BillAlertContractType.NewFee,
                    BillAlertContractSeverity.Warning,
                    title,
                    message));
        }

        foreach (var previousItem in
                 previous.Values
                     .Where(
                         item =>
                             string.Equals(
                                 item.Category,
                                 "Discount",
                                 StringComparison.OrdinalIgnoreCase) &&
                             item.Amount <
                                 0m)
                     .OrderBy(
                         item =>
                             item.Description,
                         StringComparer.OrdinalIgnoreCase))
        {
            current.TryGetValue(
                previousItem.Description,
                out var currentItem);

            var currentAmount =
                currentItem?.Amount
                ?? 0m;

            if (currentAmount <
                0m)
            {
                continue;
            }

            var discountAmount =
                Math.Abs(
                    previousItem.Amount);

            var title =
                Truncate(
                    $"{providerName}: discount removed — {previousItem.Description}",
                    MaxTitleLength);

            var message =
                Truncate(
                    $"A {FormatMoney(discountAmount)} discount labeled \"{previousItem.Description}\" was present on the previous provider statement but is absent from the latest statement. FullWorth has not assumed why the discount ended.",
                    MaxMessageLength);

            results.Add(
                new BillAlertDesiredState(
                    BillAlertContractType.RemovedDiscount,
                    BillAlertContractSeverity.Warning,
                    title,
                    message));
        }

        return results.AsReadOnly();
    }

    private static Dictionary<string, AggregatedLineItem>
        Aggregate(
            IReadOnlyList<BillLineItemEntity> lineItems)
    {
        var results =
            new Dictionary<
                string,
                AggregatedLineItem>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var item in
                 lineItems)
        {
            var description =
                item.Description.Trim();

            if (description.Length ==
                0)
            {
                continue;
            }

            if (results.TryGetValue(
                    description,
                    out var existing))
            {
                results[description] =
                    existing with
                    {
                        Amount =
                            decimal.Round(
                                existing.Amount +
                                item.Amount,
                                2,
                                MidpointRounding.AwayFromZero),

                        Category =
                            existing.Category ??
                            item.Category
                    };

                continue;
            }

            results.Add(
                description,
                new AggregatedLineItem(
                    description,
                    item.Amount,
                    item.Category));
        }

        return results;
    }

    private static void ValidateLineItemOwnership(
        Guid userId,
        Guid? expectedStatementId,
        IReadOnlyList<BillLineItemEntity> lineItems)
    {
        if (!expectedStatementId.HasValue)
        {
            if (lineItems.Count >
                0)
            {
                throw new InvalidOperationException(
                    "Line-item evidence was supplied without an expected statement.");
            }

            return;
        }

        foreach (var lineItem in
                 lineItems)
        {
            if (lineItem.UserId !=
                    userId ||
                lineItem.BillStatementId !=
                    expectedStatementId.Value)
            {
                throw new InvalidOperationException(
                    "Line-item evidence does not belong to the requested statement history.");
            }
        }
    }

    private static string FormatMoney(
        decimal amount)
    {
        return
            $"${Math.Abs(amount):0.00}";
    }

    private static string Truncate(
        string value,
        int maximumLength)
    {
        if (value.Length <=
            maximumLength)
        {
            return value;
        }

        return
            value[..(maximumLength - 1)]
            + "…";
    }

    private sealed record AggregatedLineItem(
        string Description,
        decimal Amount,
        string? Category);
}

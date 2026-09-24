using System.Globalization;
using FullWorth.API.Services.Contracts;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementPaymentDueAlertService
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    private readonly IBillStreamReadGateway
        _billStreamGateway;

    private readonly IBillAlertReconciliationGateway
        _billAlertGateway;

    public BillStatementPaymentDueAlertService(
        IBillStreamReadGateway billStreamGateway,
        IBillAlertReconciliationGateway billAlertGateway)
    {
        ArgumentNullException.ThrowIfNull(
            billStreamGateway);

        ArgumentNullException.ThrowIfNull(
            billAlertGateway);

        _billStreamGateway =
            billStreamGateway;

        _billAlertGateway =
            billAlertGateway;
    }

    public async Task ReconcileAsync(
        Guid userId,
        Guid billStreamId,
        DateOnly? dueDate,
        decimal totalAmount,
        string currencyCode,
        DateOnly today,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
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

        if (!dueDate.HasValue)
        {
            return;
        }

        if (dueDate.Value <
            today)
        {
            return;
        }

        if (totalAmount <
            0m)
        {
            throw new InvalidOperationException(
                "Payment-due alerts cannot be created from a negative statement total.");
        }

        var normalizedCurrency =
            currencyCode
                .Trim()
                .ToUpperInvariant();

        if (normalizedCurrency.Length !=
            3)
        {
            throw new InvalidOperationException(
                "Payment-due alert currency is invalid.");
        }

        var daysUntilDue =
            dueDate.Value.DayNumber -
            today.DayNumber;

        var severity =
            daysUntilDue <=
            7
                ? BillAlertContractSeverity.Warning
                : BillAlertContractSeverity.Info;

        var formattedDueDate =
            dueDate.Value.ToString(
                "MMM d, yyyy",
                CultureInfo.InvariantCulture);

        var billStream =
            await _billStreamGateway.GetOwnedAsync(
                userId,
                billStreamId,
                cancellationToken);

        if (billStream is null ||
            string.IsNullOrWhiteSpace(
                billStream.ProviderName))
        {
            throw new InvalidOperationException(
                "The owned bill stream could not be found.");
        }

        var providerName =
            billStream.ProviderName;

        var title =
            Truncate(
                $"{providerName} payment due {formattedDueDate}",
                MaxTitleLength);

        var amount =
            FormatAmount(
                totalAmount,
                normalizedCurrency);

        var timing =
            daysUntilDue switch
            {
                0 =>
                    "today",

                1 =>
                    "tomorrow",

                _ =>
                    $"on {formattedDueDate}"
            };

        var message =
            Truncate(
                $"{amount} is due {timing}. FullWorth found this due date directly on the provider statement.",
                MaxMessageLength);

        await _billAlertGateway
            .StageReconciliationAsync(
                userId,
                billStreamId,
                [
                    new BillAlertReconciliationScope(
                        BillChangeId:
                            null,

                        ManagedAlertTypes:
                            [
                                BillAlertContractType.PaymentDue
                            ],

                        DesiredAlerts:
                            [
                                new BillAlertDesiredState(
                                    BillAlertContractType.PaymentDue,
                                    severity,
                                    title,
                                    message)
                            ],

                        Mode:
                            BillAlertReconciliationMode.UpsertDesiredIdentities)
                ],
                removeBillChangeIds:
                    [],
                now,
                cancellationToken);
    }

    private static string FormatAmount(
        decimal amount,
        string currencyCode)
    {
        if (string.Equals(
                currencyCode,
                "USD",
                StringComparison.Ordinal))
        {
            return
                "$" +
                amount.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture);
        }

        return
            $"{currencyCode} {amount:0.00}";
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
}

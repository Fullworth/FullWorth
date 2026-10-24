using System.Globalization;
using FullWorth.Core.Formatting;
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

    // Clearing an unsupported obligation needs ownership, not fabricated
    // amount/currency/provider-name facts solely to pass creation validation.
    public async Task ClearAsync(
        Guid userId, Guid billStreamId, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || billStreamId == Guid.Empty)
            throw new ArgumentException("A valid owner and bill identity are required.");
        if (await _billStreamGateway.GetOwnedAsync(userId, billStreamId, cancellationToken) is null)
            throw new InvalidOperationException("The owned bill stream could not be found.");
        await _billAlertGateway.StageReconciliationAsync(
            userId, billStreamId,
            [new BillAlertReconciliationScope(null, [BillAlertContractType.PaymentDue], [],
                BillAlertReconciliationMode.SingleManagedSlot)],
            [], now, cancellationToken);
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
            3 || normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
        {
            throw new InvalidOperationException(
                "Payment-due alert currency is invalid.");
        }

        if (!dueDate.HasValue || dueDate.Value < today || totalAmount == 0m)
        {
            await _billAlertGateway.StageReconciliationAsync(
                userId, billStreamId,
                [new BillAlertReconciliationScope(
                    null, [BillAlertContractType.PaymentDue], [],
                    BillAlertReconciliationMode.SingleManagedSlot)],
                [], now, cancellationToken);
            return;
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
                            BillAlertReconciliationMode.SingleManagedSlot)
                ],
                removeBillChangeIds:
                    [],
                now,
                cancellationToken);
    }

    private static string FormatAmount(decimal amount, string currencyCode) =>
        FinancialAmountFormatter.Format(amount, currencyCode);

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


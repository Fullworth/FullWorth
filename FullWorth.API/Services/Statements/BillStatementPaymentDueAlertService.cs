using System.Globalization;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Statements;

public sealed class BillStatementPaymentDueAlertService
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    private readonly FullWorthDbContext
        _dbContext;

    private readonly IBillStreamReadGateway
        _billStreamGateway;

    public BillStatementPaymentDueAlertService(
        FullWorthDbContext dbContext,
        IBillStreamReadGateway billStreamGateway)
    {
        _dbContext =
            dbContext;

        _billStreamGateway =
            billStreamGateway;
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

        /*
         * No explicit provider due date means no alert.
         *
         * FullWorth does not guess due dates.
         */
        if (!dueDate.HasValue)
        {
            return;
        }

        /*
         * An old provider due date is not enough evidence to claim
         * that money is currently owed or overdue.
         */
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
                ? BillAlertSeverity.Warning
                : BillAlertSeverity.Info;

        var formattedDueDate =
            dueDate.Value.ToString(
                "MMM d, yyyy",
                CultureInfo.InvariantCulture);

        /*
         * The title acts as the semantic identity for a payment-due
         * event within a Bill Stream.
         *
         * Bill Stream ownership/provider context crosses the Bills-owned
         * read boundary. Alert persistence remains a separate migration
         * boundary until it is moved behind a Bills-owned alert contract.
         */
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

        var paymentAlerts =
            await _dbContext.BillAlerts
                .Where(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            billStreamId &&
                        alert.AlertType ==
                            BillAlertType.PaymentDue)
                .OrderBy(
                    alert =>
                        alert.CreatedAtUtc)
                .ThenBy(
                    alert =>
                        alert.Id)
                .ToListAsync(
                    cancellationToken);

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

        var matchingAlerts =
            paymentAlerts
                .Where(
                    alert =>
                        string.Equals(
                            alert.Title,
                            title,
                            StringComparison.Ordinal))
                .ToList();

        if (matchingAlerts.Count ==
            0)
        {
            _dbContext.BillAlerts.Add(
                new BillAlertEntity
                {
                    UserId =
                        userId,

                    BillStreamId =
                        billStreamId,

                    BillChangeId =
                        null,

                    AlertType =
                        BillAlertType.PaymentDue,

                    Severity =
                        severity,

                    Title =
                        title,

                    Message =
                        message,

                    IsRead =
                        false,

                    IsDismissed =
                        false,

                    CreatedAtUtc =
                        now,

                    UpdatedAtUtc =
                        now
                });

            return;
        }

        var primaryAlert =
            matchingAlerts[0];

        var changed =
            primaryAlert.Severity !=
                severity ||
            !string.Equals(
                primaryAlert.Message,
                message,
                StringComparison.Ordinal);

        if (changed)
        {
            primaryAlert.Severity =
                severity;

            primaryAlert.Message =
                message;

            primaryAlert.IsRead =
                false;

            primaryAlert.IsDismissed =
                false;

            primaryAlert.UpdatedAtUtc =
                now;
        }

        /*
         * Defensive cleanup if older code or a race ever produced
         * duplicate alerts for this same due event.
         */
        if (matchingAlerts.Count >
            1)
        {
            _dbContext.BillAlerts.RemoveRange(
                matchingAlerts.Skip(
                    1));
        }
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
                $"${amount:0.00}";
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
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class RecurringBillDiscoveryAlertService
{
    private const int MaxTitleLength =
        300;

    private const int MaxMessageLength =
        2000;

    private readonly FullWorthDbContext
        _dbContext;

    public RecurringBillDiscoveryAlertService(
        FullWorthDbContext dbContext)
    {
        _dbContext =
            dbContext;
    }

    public async Task<bool> EnsureNewBillAlertAsync(
        Guid userId,
        BillStreamEntity billStream,
        int matchingTransactionCount,
        DateTimeOffset now,
        CancellationToken cancellationToken = default,
        bool newlyAddedStream = false)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(
            billStream);

        if (billStream.Id ==
            Guid.Empty)
        {
            throw new InvalidOperationException(
                "The Bill Stream does not have an ID.");
        }

        if (billStream.UserId !=
            userId)
        {
            throw new InvalidOperationException(
                "The Bill Stream does not belong to the requested user.");
        }

        if (billStream.Source !=
            BillStreamSource.AutomaticDiscovery)
        {
            throw new InvalidOperationException(
                "New recurring-bill alerts may only be created for automatically discovered Bill Streams.");
        }

        if (matchingTransactionCount <=
            0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(matchingTransactionCount),
                "At least one matching transaction is required.");
        }

        if (string.IsNullOrWhiteSpace(
                billStream.ProviderName))
        {
            throw new InvalidOperationException(
                "The Bill Stream does not have a provider name.");
        }

        /*
         * Check tracked alerts first.
         *
         * This prevents duplicate alerts even if this method is called
         * twice before the DbContext's final SaveChangesAsync.
         */
        var alreadyTracked =
            _dbContext.BillAlerts.Local
                .Any(
                    alert =>
                        alert.UserId ==
                            userId &&
                        alert.BillStreamId ==
                            billStream.Id &&
                        alert.AlertType ==
                            BillAlertType.NewBill);

        if (alreadyTracked)
        {
            return false;
        }

        /*
         * A stream created by recurring discovery is already tracked as
         * Added in this unit of work and has a freshly generated ID. In that
         * narrow case a persisted duplicate cannot exist, so skip the
         * otherwise-required database existence query.
         *
         * Keep the generic path fully defensive for existing streams and
         * direct callers.
         */
        if (newlyAddedStream)
        {
            if (_dbContext.Entry(
                    billStream).State !=
                EntityState.Added)
            {
                throw new InvalidOperationException(
                    "The new-stream alert fast path requires an Added Bill Stream.");
            }
        }
        else
        {
            var alreadyPersisted =
                await _dbContext.BillAlerts
                    .AsNoTracking()
                    .AnyAsync(
                        alert =>
                            alert.UserId ==
                                userId &&
                            alert.BillStreamId ==
                                billStream.Id &&
                            alert.AlertType ==
                                BillAlertType.NewBill,
                        cancellationToken);

            if (alreadyPersisted)
            {
                return false;
            }
        }

        var providerName =
            billStream.ProviderName.Trim();

        var title =
            Truncate(
                $"New recurring bill: {providerName}",
                MaxTitleLength);

        var transactionWord =
            matchingTransactionCount ==
            1
                ? "transaction"
                : "transactions";

        var message =
            Truncate(
                $"FullWorth found {matchingTransactionCount} posted bank {transactionWord} matching a recurring pattern for {providerName}. It has been added to Bills and will be monitored automatically. This is transaction-based discovery; a provider statement has not yet been used to explain the bill.",
                MaxMessageLength);

        _dbContext.BillAlerts.Add(
            new BillAlertEntity
            {
                UserId =
                    userId,

                BillStreamId =
                    billStream.Id,

                BillStream =
                    billStream,

                BillChangeId =
                    null,

                AlertType =
                    BillAlertType.NewBill,

                Severity =
                    BillAlertSeverity.Info,

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

        return true;
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
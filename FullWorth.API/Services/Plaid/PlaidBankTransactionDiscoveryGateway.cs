using FullWorth.API.Data;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankTransactionDiscoveryGateway(
    FullWorthDbContext dbContext)
    : IBankTransactionDiscoveryGateway
{
    public async Task<IReadOnlyList<BankTransactionDiscoveryRecord>>
        GetDiscoveryTransactionsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        return await dbContext.BankTransactions
            .AsNoTracking()
            .Where(
                transaction =>
                    transaction.UserId ==
                        userId &&
                    !transaction.IsRemoved)
            .OrderBy(
                transaction =>
                    transaction.PostedDate)
            .ThenBy(
                transaction =>
                    transaction.Id)
            .Select(
                transaction =>
                    new BankTransactionDiscoveryRecord(
                        transaction.Id,
                        transaction.BillStreamId,
                        transaction.Name,
                        transaction.MerchantName,
                        transaction.Amount,
                        transaction.PostedDate,
                        transaction.IsPending,
                        transaction.CategoryPrimary,
                        transaction.CategoryDetailed))
            .ToListAsync(
                cancellationToken);
    }

    public async Task StageBillStreamAssignmentsAsync(
        Guid userId,
        IReadOnlyCollection<BankTransactionBillStreamAssignment> assignments,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(
            userId);

        ArgumentNullException.ThrowIfNull(
            assignments);

        cancellationToken.ThrowIfCancellationRequested();

        if (assignments.Count ==
            0)
        {
            return;
        }

        var assignmentsByTransactionId =
            new Dictionary<
                Guid,
                BankTransactionBillStreamAssignment>();

        foreach (var assignment in
                 assignments)
        {
            if (assignment.TransactionId ==
                Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction IDs are required.",
                    nameof(assignments));
            }

            if (!assignmentsByTransactionId.TryAdd(
                    assignment.TransactionId,
                    assignment))
            {
                throw new ArgumentException(
                    "Duplicate transaction assignments are not allowed.",
                    nameof(assignments));
            }
        }

        var transactionIds =
            assignmentsByTransactionId.Keys
                .ToArray();

        var transactions =
            await dbContext.BankTransactions
                .Where(
                    transaction =>
                        transaction.UserId ==
                            userId &&
                        transactionIds.Contains(
                            transaction.Id))
                .ToListAsync(
                    cancellationToken);

        if (transactions.Count !=
            assignmentsByTransactionId.Count)
        {
            /*
             * Keep cross-user rows indistinguishable from missing rows.
             */
            throw new KeyNotFoundException(
                "One or more bank transactions were not found.");
        }

        foreach (var transaction in
                 transactions)
        {
            var assignment =
                assignmentsByTransactionId[
                    transaction.Id];

            if (transaction.BillStreamId !=
                assignment.ExpectedBillStreamId)
            {
                throw new InvalidOperationException(
                    "A bank transaction bill link changed during recurring-bill discovery.");
            }

            if (transaction.BillStreamId ==
                assignment.BillStreamId)
            {
                continue;
            }

            transaction.BillStreamId =
                assignment.BillStreamId;

            transaction.UpdatedAtUtc =
                updatedAtUtc;
        }

        /*
         * Do not call SaveChangesAsync here.
         *
         * FullWorth currently uses one scoped modular-monolith unit of work.
         * Bills commits its Bill Streams/alerts and these staged bank-link
         * changes together so recurring discovery remains atomic.
         */
    }

    private static void ValidateUserId(
        Guid userId)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Bills;

public sealed class BillTransactionAssociationGateway(
    FullWorthDbContext dbContext)
    : IBillTransactionAssociationGateway
{
    public async Task<IReadOnlyList<BillTransactionAssociationRecord>> GetAsync(
        Guid userId,
        IReadOnlyCollection<Guid> bankTransactionIds,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(bankTransactionIds);

        if (bankTransactionIds.Count == 0)
        {
            return Array.Empty<BillTransactionAssociationRecord>();
        }

        if (bankTransactionIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "Transaction IDs are required.",
                nameof(bankTransactionIds));
        }

        var transactionIds =
            bankTransactionIds
                .Distinct()
                .ToArray();

        return await dbContext.BillTransactionAssociations
            .AsNoTracking()
            .Where(
                association =>
                    association.UserId == userId &&
                    transactionIds.Contains(
                        association.BankTransactionId))
            .OrderBy(
                association =>
                    association.BankTransactionId)
            .Select(
                association =>
                    new BillTransactionAssociationRecord(
                        association.BankTransactionId,
                        association.BillStreamId))
            .ToListAsync(
                cancellationToken);
    }

    public async Task StageAssignmentsAsync(
        Guid userId,
        IReadOnlyCollection<BillTransactionAssociationAssignment> assignments,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateUserId(userId);
        ArgumentNullException.ThrowIfNull(assignments);
        cancellationToken.ThrowIfCancellationRequested();

        if (assignments.Count == 0)
        {
            return;
        }

        var assignmentsByTransactionId =
            new Dictionary<Guid, BillTransactionAssociationAssignment>();

        foreach (var assignment in assignments)
        {
            if (assignment.BankTransactionId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Transaction IDs are required.",
                    nameof(assignments));
            }

            if (!assignmentsByTransactionId.TryAdd(
                    assignment.BankTransactionId,
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

        var existingAssociations =
            await dbContext.BillTransactionAssociations
                .Where(
                    association =>
                        association.UserId == userId &&
                        transactionIds.Contains(
                            association.BankTransactionId))
                .ToDictionaryAsync(
                    association =>
                        association.BankTransactionId,
                    cancellationToken);

        var requestedBillStreamIds =
            assignments
                .Where(assignment => assignment.BillStreamId.HasValue)
                .Select(assignment => assignment.BillStreamId!.Value)
                .Distinct()
                .ToArray();

        if (requestedBillStreamIds.Length > 0)
        {
            var requestedBillStreamIdSet =
                requestedBillStreamIds
                    .ToHashSet();

            var trackedOwnedBillStreamIds =
                dbContext.ChangeTracker
                    .Entries<BillStreamEntity>()
                    .Where(
                        entry =>
                            entry.State != EntityState.Deleted &&
                            entry.Entity.UserId == userId &&
                            requestedBillStreamIdSet.Contains(
                                entry.Entity.Id))
                    .Select(entry => entry.Entity.Id)
                    .ToHashSet();

            var persistedIdsToVerify =
                requestedBillStreamIds
                    .Where(
                        id =>
                            !trackedOwnedBillStreamIds.Contains(id))
                    .ToArray();

            if (persistedIdsToVerify.Length > 0)
            {
                var persistedOwnedBillStreamIds =
                    await dbContext.BillStreams
                        .AsNoTracking()
                        .Where(
                            stream =>
                                stream.UserId == userId &&
                                persistedIdsToVerify.Contains(stream.Id))
                        .Select(stream => stream.Id)
                        .ToListAsync(cancellationToken);

                trackedOwnedBillStreamIds.UnionWith(
                    persistedOwnedBillStreamIds);
            }

            if (trackedOwnedBillStreamIds.Count !=
                requestedBillStreamIds.Length)
            {
                throw new KeyNotFoundException(
                    "One or more bill streams were not found.");
            }
        }

        foreach (var assignment in assignments)
        {
            existingAssociations.TryGetValue(
                assignment.BankTransactionId,
                out var existingAssociation);

            var currentBillStreamId =
                existingAssociation?.BillStreamId;

            if (currentBillStreamId != assignment.ExpectedBillStreamId)
            {
                throw new InvalidOperationException(
                    "A bank transaction bill association changed during recurring-bill discovery.");
            }

            if (currentBillStreamId == assignment.BillStreamId)
            {
                continue;
            }

            if (!assignment.BillStreamId.HasValue)
            {
                if (existingAssociation is not null)
                {
                    dbContext.BillTransactionAssociations.Remove(
                        existingAssociation);
                }

                continue;
            }

            if (existingAssociation is null)
            {
                dbContext.BillTransactionAssociations.Add(
                    new BillTransactionAssociationEntity
                    {
                        UserId = userId,
                        BankTransactionId = assignment.BankTransactionId,
                        BillStreamId = assignment.BillStreamId.Value,
                        CreatedAtUtc = updatedAtUtc,
                        UpdatedAtUtc = updatedAtUtc
                    });

                continue;
            }

            existingAssociation.BillStreamId =
                assignment.BillStreamId.Value;

            existingAssociation.UpdatedAtUtc =
                updatedAtUtc;
        }

        /*
         * Do not save here. Bills owns both Bill Streams and these
         * associations, and the caller commits them in the same scoped
         * modular-monolith unit of work.
         */
    }

    private static void ValidateUserId(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidBankConnectionReadGateway(
    FullWorthDbContext dbContext)
    : IBankConnectionReadGateway
{
    public async Task<IReadOnlyList<string>>
        GetAttentionInstitutionNamesAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException(
                "User ID is required.",
                nameof(userId));
        }

        return await dbContext.BankConnections
            .AsNoTracking()
            .Where(
                connection =>
                    connection.UserId == userId &&
                    connection.Status ==
                        BankConnectionStatus.RequiresAttention)
            .OrderBy(
                connection =>
                    connection.InstitutionName)
            .ThenBy(
                connection =>
                    connection.Id)
            .Select(
                connection =>
                    connection.InstitutionName)
            .ToListAsync(
                cancellationToken);
    }

    public Task<IReadOnlyList<Guid>> GetDueUserIdsAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        CancellationToken cancellationToken = default) =>
        GetDueUserIdsPageAsync(now, refreshCadence, maxUsers, 0, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetDueUserIdsPageAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        int offset,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        if (maxUsers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUsers));
        }

        return await QueryDueUserIds(now, refreshCadence)
            .OrderBy(userId => userId)
            .Skip(offset)
            .Take(maxUsers)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> GetDueUserIdsAfterAsync(
        DateTimeOffset now,
        TimeSpan refreshCadence,
        int maxUsers,
        Guid? afterUserId,
        CancellationToken cancellationToken = default) =>
        await QueryDueUserIdsAfter(now, refreshCadence, maxUsers, afterUserId)
            .ToListAsync(cancellationToken);

    // Keep the production query inspectable by provider-translation tests.
    internal IQueryable<Guid> QueryDueUserIdsAfter(
        DateTimeOffset now, TimeSpan refreshCadence, int maxUsers, Guid? afterUserId)
    {
        if (maxUsers <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxUsers));
        }

        var query = QueryDueUserIds(now, refreshCadence);
        if (afterUserId.HasValue)
        {
            var previous = afterUserId.Value;
            query = query.Where(userId => userId.CompareTo(previous) > 0);
        }

        return query.OrderBy(userId => userId).Take(maxUsers);
    }

    private IQueryable<Guid> QueryDueUserIds(DateTimeOffset now, TimeSpan refreshCadence)
    {
        if (refreshCadence <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshCadence));
        }

        var cutoff = now - refreshCadence;
        return dbContext.BankConnections
            .AsNoTracking()
            .Where(connection =>
                connection.Status == BankConnectionStatus.Active &&
                connection.ProtectedPlaidAccessToken != null &&
                connection.ProtectedPlaidAccessToken != string.Empty &&
                (!connection.LastSuccessfulSyncAtUtc.HasValue ||
                    connection.LastSuccessfulSyncAtUtc.Value <= cutoff))
            .Select(connection => connection.UserId)
            .Distinct();
    }
}

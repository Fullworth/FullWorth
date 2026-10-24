using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.API.Services.Plaid;

internal static class PlaidSyncConnectionGuard
{
    public static async Task EnsureCurrentAsync(FullWorthDbContext db, Guid userId,
        BankConnectionEntity connection, CancellationToken cancellationToken)
    {
        // A tracked instance can be stale even when a query resolved its owner.
        // Read durable lifecycle evidence before sending a provider credential.
        var current = await db.BankConnections.AsNoTracking()
            .Where(bank => bank.Id == connection.Id && bank.UserId == userId)
            .Select(bank => new { bank.Status, bank.ProtectedPlaidAccessToken })
            .SingleOrDefaultAsync(cancellationToken);
        if (current is null)
            throw new KeyNotFoundException("Bank connection was not found.");
        if (current.Status == BankConnectionStatus.Disconnected ||
            connection.Status == BankConnectionStatus.Disconnected)
            throw new InvalidOperationException("Disconnected bank connections cannot synchronize.");
        if (!string.Equals(current.ProtectedPlaidAccessToken, connection.ProtectedPlaidAccessToken,
                StringComparison.Ordinal))
            throw new InvalidOperationException("Bank connection changed before synchronization. Reload and try again.");

        // Status and protected credential concurrency tokens enforce the same
        // boundary atomically if another writer wins after this read.
    }
}

using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FullWorth.API.Services.Plaid;

public sealed class PlaidConnectionDisconnectService
{
    private const string ItemNotFoundErrorCode =
        "ITEM_NOT_FOUND";

    private readonly FullWorthDbContext
        _dbContext;

    private readonly PlaidApiClient
        _plaidApiClient;

    private readonly PlaidTokenProtector
        _tokenProtector;

    public PlaidConnectionDisconnectService(
        FullWorthDbContext dbContext,
        PlaidApiClient plaidApiClient,
        PlaidTokenProtector tokenProtector)
    {
        ArgumentNullException.ThrowIfNull(
            dbContext);

        ArgumentNullException.ThrowIfNull(
            plaidApiClient);

        ArgumentNullException.ThrowIfNull(
            tokenProtector);

        _dbContext =
            dbContext;

        _plaidApiClient =
            plaidApiClient;

        _tokenProtector =
            tokenProtector;
    }

    public async Task<bool> DisconnectAsync(
        Guid userId,
        Guid connectionId,
        CancellationToken cancellationToken = default)
    {
        if (userId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid user ID is required.",
                nameof(userId));
        }

        if (connectionId ==
            Guid.Empty)
        {
            throw new ArgumentException(
                "A valid bank connection ID is required.",
                nameof(connectionId));
        }

        cancellationToken.ThrowIfCancellationRequested();

        /*
         * Resolve the resource using both UserId and connection ID.
         *
         * Cross-user manipulation therefore returns the same result as an
         * unknown connection.
         */
        var connection =
            await _dbContext.BankConnections
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                            connectionId &&
                        item.UserId ==
                            userId,
                    cancellationToken);

        if (connection is null)
        {
            return false;
        }

        /*
         * If any protected provider credential remains, attempt provider
         * revocation even if local state already says Disconnected.
         *
         * This repairs a possible partially completed previous operation.
         */
        if (!string.IsNullOrWhiteSpace(
                connection.ProtectedPlaidAccessToken))
        {
            var accessToken =
                _tokenProtector.Unprotect(
                    connection.ProtectedPlaidAccessToken);

            try
            {
                using var response =
                    await _plaidApiClient.PostAsync(
                        "item/remove",
                        new
                        {
                            access_token =
                                accessToken
                        },
                        cancellationToken);
            }
            catch (PlaidApiException exception)
                when (string.Equals(
                    exception.ErrorCode,
                    ItemNotFoundErrorCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                /*
                 * Provider state is already equivalent to the requested
                 * result. Continue with local credential removal.
                 */
            }
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Resolve every owned account before staging any disconnect state.
        // Provider revocation may already have succeeded, so a failed local
        // operation must retain the credential for an ITEM_NOT_FOUND retry.
        var accounts =
            await _dbContext.BankAccounts
                .Where(
                    account =>
                        account.UserId ==
                            userId &&
                        account.BankConnectionId ==
                            connectionId)
                .ToListAsync(
                    cancellationToken);

        var snapshots = accounts
            .Select(account => new DisconnectEntrySnapshot(_dbContext.Entry(account)))
            .Append(new DisconnectEntrySnapshot(_dbContext.Entry(connection)))
            .ToArray();

        try
        {
            var now =
                DateTimeOffset.UtcNow;

            /*
             * Once provider revocation succeeds, remove the local credential
             * and synchronization cursor before reporting success.
             */
            connection.ProtectedPlaidAccessToken =
                null;

            connection.TransactionsCursor =
                null;

            connection.Status =
                BankConnectionStatus.Disconnected;

            connection.UpdatedAtUtc =
                now;

            /*
             * Historical accounts and transactions remain available for
             * FullWorth history, but disconnected accounts cannot be treated as
             * currently active bank sources.
             */
            foreach (var account in
                     accounts)
            {
                account.IsActive =
                    false;

                account.UpdatedAtUtc =
                    now;
            }

            /*
             * The credential removal, disconnected status, and account
             * deactivation are one local database commit.
             *
             * If cancellation or a database failure occurs after Plaid has
             * already removed the Item, retrying is safe: ITEM_NOT_FOUND is
             * treated as successful provider revocation.
             */
            await _dbContext.SaveChangesAsync(
                cancellationToken);
        }
        catch
        {
            // A failed/canceled SaveChanges leaves EF values staged. Prevent
            // unrelated later saves from committing this failed disconnect,
            // without discarding pre-existing caller edits.
            foreach (var snapshot in snapshots)
            {
                snapshot.Restore();
            }

            throw;
        }

        return true;
    }

    private sealed class DisconnectEntrySnapshot
    {
        private readonly EntityEntry _entry;
        private readonly PropertyValues _current;
        private readonly PropertyValues _original;
        private readonly EntityState _state;
        private readonly HashSet<string> _modified;

        public DisconnectEntrySnapshot(EntityEntry entry)
        {
            _entry = entry;
            _current = entry.CurrentValues.Clone();
            _original = entry.OriginalValues.Clone();
            _state = entry.State;
            _modified = entry.Properties
                .Where(property => property.IsModified)
                .Select(property => property.Metadata.Name)
                .ToHashSet(StringComparer.Ordinal);
        }

        public void Restore()
        {
            _entry.CurrentValues.SetValues(_current);
            _entry.OriginalValues.SetValues(_original);
            _entry.State = _state;

            if (_state == EntityState.Modified)
            {
                foreach (var property in _entry.Properties)
                {
                    property.IsModified = _modified.Contains(property.Metadata.Name);
                }
            }
        }
    }
}
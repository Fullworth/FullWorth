using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;

namespace FullWorth.Web.Infrastructure;

public sealed class ProtectedDistributedTicketStore(
    IDistributedCache cache,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider)
    : ITicketStore
{
    private const string KeyPrefix =
        "auth-ticket:";

    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector(
            "FullWorth.Web.AuthenticationTicketStore",
            "v1");

    public Task<string> StoreAsync(
        AuthenticationTicket ticket)
    {
        return StoreAsync(
            ticket,
            CancellationToken.None);
    }

    public async Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            ticket);

        var key =
            KeyPrefix +
            Convert.ToHexString(
                    RandomNumberGenerator.GetBytes(32))
                .ToLowerInvariant();

        await WriteAsync(
            key,
            ticket,
            cancellationToken);

        return key;
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket)
    {
        return RenewAsync(
            key,
            ticket,
            CancellationToken.None);
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        ValidateKey(key);
        ArgumentNullException.ThrowIfNull(ticket);

        return WriteAsync(
            key,
            ticket,
            cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key)
    {
        return RetrieveAsync(
            key,
            CancellationToken.None);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
        {
            return null;
        }

        var protectedTicket =
            await cache.GetAsync(
                key,
                cancellationToken);

        if (protectedTicket is null)
        {
            return null;
        }

        try
        {
            var serializedTicket =
                _protector.Unprotect(
                    protectedTicket);

            var ticket =
                TicketSerializer.Default.Deserialize(
                    serializedTicket);

            if (ticket is null)
            {
                await cache.RemoveAsync(
                    key,
                    cancellationToken);

                return null;
            }

            if (ticket.Properties.ExpiresUtc is
                    { } expiresAtUtc &&
                expiresAtUtc <=
                    timeProvider.GetUtcNow())
            {
                await cache.RemoveAsync(
                    key,
                    cancellationToken);

                return null;
            }

            return ticket;
        }
        catch (CryptographicException)
        {
            await cache.RemoveAsync(
                key,
                cancellationToken);

            return null;
        }
    }

    public Task RemoveAsync(
        string key)
    {
        return RemoveAsync(
            key,
            CancellationToken.None);
    }

    public Task RemoveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (!IsValidKey(key))
        {
            return Task.CompletedTask;
        }

        return cache.RemoveAsync(
            key,
            cancellationToken);
    }

    private async Task WriteAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        var expiresAtUtc =
            ticket.Properties.ExpiresUtc
            ?? throw new InvalidOperationException(
                "Web authentication tickets require an absolute expiration.");

        if (expiresAtUtc <=
            timeProvider.GetUtcNow())
        {
            throw new InvalidOperationException(
                "Expired Web authentication tickets cannot be stored.");
        }

        var serializedTicket =
            TicketSerializer.Default.Serialize(
                ticket);

        var protectedTicket =
            _protector.Protect(
                serializedTicket);

        await cache.SetAsync(
            key,
            protectedTicket,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpiration =
                    expiresAtUtc
            },
            cancellationToken);
    }

    private static void ValidateKey(
        string key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException(
                "The Web session key is invalid.",
                nameof(key));
        }
    }

    private static bool IsValidKey(
        string? key)
    {
        if (string.IsNullOrWhiteSpace(
                key) ||
            !key.StartsWith(
                KeyPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        var suffix =
            key[KeyPrefix.Length..];

        return suffix.Length == 64 &&
               suffix.All(
                   character =>
                       character is >= '0' and <= '9' ||
                       character is >= 'a' and <= 'f');
    }
}

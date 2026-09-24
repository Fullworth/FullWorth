using System.Security.Claims;
using System.Text;
using FullWorth.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class ProtectedDistributedTicketStoreTests
{
    [Fact]
    public async Task StoredTicket_IsEncryptedAtRest_AndRoundTrips()
    {
        const string accessToken =
            "access-token-that-must-not-appear-in-cache";

        const string refreshToken =
            "refresh-token-that-must-not-appear-in-cache";

        var cache =
            CreateCache();

        var store =
            new ProtectedDistributedTicketStore(
                cache,
                new EphemeralDataProtectionProvider(),
                TimeProvider.System);

        var ticket =
            CreateTicket(
                accessToken,
                refreshToken,
                DateTimeOffset.UtcNow.AddHours(1));

        var key =
            await store.StoreAsync(
                ticket);

        var cachedBytes =
            await cache.GetAsync(
                key);

        Assert.NotNull(
            cachedBytes);

        var cachedText =
            Encoding.UTF8.GetString(
                cachedBytes!);

        Assert.DoesNotContain(
            accessToken,
            cachedText,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            refreshToken,
            cachedText,
            StringComparison.Ordinal);

        var roundTrip =
            await store.RetrieveAsync(
                key);

        Assert.NotNull(
            roundTrip);

        Assert.Equal(
            accessToken,
            roundTrip!.Properties.GetTokenValue(
                "access_token"));

        Assert.Equal(
            refreshToken,
            roundTrip.Properties.GetTokenValue(
                "refresh_token"));
    }

    [Fact]
    public async Task SessionAccessor_ResolvesOpaqueCookieReferenceToLatestStoredTicket()
    {
        const string accessToken =
            "server-only-access-token";

        const string refreshToken =
            "server-only-refresh-token";

        var dataProtection =
            new EphemeralDataProtectionProvider();

        var store =
            new ProtectedDistributedTicketStore(
                CreateCache(),
                dataProtection,
                TimeProvider.System);

        var storedTicket =
            CreateTicket(
                accessToken,
                refreshToken,
                DateTimeOffset.UtcNow.AddHours(1));

        var sessionKey =
            await store.StoreAsync(
                storedTicket);

        var cookieOptions =
            new CookieAuthenticationOptions
            {
                Cookie =
                {
                    Name =
                        "__Host-BillWatch.Web.Auth"
                },

                CookieManager =
                    new ChunkingCookieManager(),

                TicketDataFormat =
                    new TicketDataFormat(
                        dataProtection.CreateProtector(
                            "FullWorth.Tests.CookieReference"))
            };

        var referencePrincipal =
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [
                        new Claim(
                            "Microsoft.AspNetCore.Authentication.Cookies-SessionId",
                            sessionKey)
                    ],
                    CookieAuthenticationDefaults
                        .AuthenticationScheme));

        var referenceTicket =
            new AuthenticationTicket(
                referencePrincipal,
                new AuthenticationProperties(),
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var protectedCookie =
            cookieOptions.TicketDataFormat.Protect(
                referenceTicket);

        var context =
            new DefaultHttpContext();

        context.Request.Headers.Cookie =
            $"{cookieOptions.Cookie.Name}={protectedCookie}";

        var accessor =
            new WebSessionTicketAccessor(
                new StaticOptionsMonitor<
                    CookieAuthenticationOptions>(
                    cookieOptions),
                store);

        var snapshot =
            await accessor.ReadLatestAsync(
                context);

        Assert.NotNull(
            snapshot);

        Assert.Equal(
            accessToken,
            snapshot!.AccessToken);

        Assert.Equal(
            refreshToken,
            snapshot.RefreshToken);

        Assert.DoesNotContain(
            accessToken,
            protectedCookie,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            refreshToken,
            protectedCookie,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemovedTicket_CannotBeRetrieved()
    {
        var store =
            new ProtectedDistributedTicketStore(
                CreateCache(),
                new EphemeralDataProtectionProvider(),
                TimeProvider.System);

        var key =
            await store.StoreAsync(
                CreateTicket(
                    "access",
                    "refresh",
                    DateTimeOffset.UtcNow.AddHours(1)));

        await store.RemoveAsync(
            key);

        var ticket =
            await store.RetrieveAsync(
                key);

        Assert.Null(
            ticket);
    }

    [Fact]
    public async Task TamperedTicket_FailsClosedAndIsRemoved()
    {
        var cache =
            CreateCache();

        var store =
            new ProtectedDistributedTicketStore(
                cache,
                new EphemeralDataProtectionProvider(),
                TimeProvider.System);

        var key =
            await store.StoreAsync(
                CreateTicket(
                    "access",
                    "refresh",
                    DateTimeOffset.UtcNow.AddHours(1)));

        var bytes =
            await cache.GetAsync(
                key);

        Assert.NotNull(
            bytes);

        bytes![0] ^= 0x7f;

        await cache.SetAsync(
            key,
            bytes,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow =
                    TimeSpan.FromHours(1)
            });

        var ticket =
            await store.RetrieveAsync(
                key);

        Assert.Null(
            ticket);

        Assert.Null(
            await cache.GetAsync(
                key));
    }

    private static MemoryDistributedCache
        CreateCache()
    {
        return new MemoryDistributedCache(
            Options.Create(
                new MemoryDistributedCacheOptions()));
    }

    private static AuthenticationTicket
        CreateTicket(
            string accessToken,
            string refreshToken,
            DateTimeOffset expiresAtUtc)
    {
        var identity =
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        Guid.NewGuid()
                            .ToString("D"))
                ],
                CookieAuthenticationDefaults
                    .AuthenticationScheme);

        var properties =
            new AuthenticationProperties
            {
                ExpiresUtc =
                    expiresAtUtc
            };

        properties.StoreTokens(
            [
                new AuthenticationToken
                {
                    Name =
                        "access_token",

                    Value =
                        accessToken
                },

                new AuthenticationToken
                {
                    Name =
                        "refresh_token",

                    Value =
                        refreshToken
                }
            ]);

        return new AuthenticationTicket(
            new ClaimsPrincipal(
                identity),
            properties,
            CookieAuthenticationDefaults
                .AuthenticationScheme);
    }
    private sealed class StaticOptionsMonitor<T>(
        T value)
        : IOptionsMonitor<T>
    {
        public T CurrentValue =>
            value;

        public T Get(
            string? name)
        {
            return value;
        }

        public IDisposable? OnChange(
            Action<T, string?> listener)
        {
            return null;
        }
    }
}

using FullWorth.API.Services.Identity;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace FullWorth.Tests.Security;

public sealed class PendingPasswordLoginTests
{
    [Fact]
    public async Task ProofHasBoundedAttemptsOriginalExpiryAndAtomicConsumption()
    {
        var clock = new Clock();
        var store = new PendingPasswordLoginStore(clock);
        var first = store.Create(Guid.NewGuid(), "stamp")!;
        var consumed = await Task.WhenAll(Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => store.Consume(first.LoginId))));
        var proof = Assert.Single(consumed, p => p is not null)!;
        Assert.Equal(clock.Now + PendingPasswordLoginStore.Lifetime, first.ExpiresAt);
        var second = store.Retry(proof)!;
        Assert.NotEqual(first.LoginId, second.LoginId);
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
        Assert.Null(store.Consume(first.LoginId));
        var third = store.Retry(store.Consume(second.LoginId)!)!;
        Assert.Null(store.Retry(store.Consume(third.LoginId)!));
    }

    [Fact]
    public void ExpiredProofsCannotBeReadConsumedOrRenewed()
    {
        var clock = new Clock();
        var store = new PendingPasswordLoginStore(clock);
        var issued = store.Create(Guid.NewGuid(), "stamp")!;
        var proof = store.Read(issued.LoginId)!;
        clock.Now += PendingPasswordLoginStore.Lifetime;
        Assert.Null(store.Read(issued.LoginId));
        Assert.Null(store.Consume(issued.LoginId));
        Assert.Null(store.Retry(proof));
    }

    [Fact]
    public void CapacityIsBoundedAndReclaimsExpiredEntries()
    {
        var clock = new Clock();
        var store = new PendingPasswordLoginStore(clock);
        for (var i = 0; i < 2048; i++) Assert.NotNull(store.Create(Guid.NewGuid(), "stamp"));
        Assert.Null(store.Create(Guid.NewGuid(), "stamp"));
        clock.Now += PendingPasswordLoginStore.Lifetime;
        Assert.NotNull(store.Create(Guid.NewGuid(), "stamp"));
    }

    [Fact]
    public void PendingCookieIsOpaqueHostOnlySecureHttpOnlyAndCannotBeTamperedWith()
    {
        var clock = new Clock();
        var service = new PendingPasswordLoginCookie(new EphemeralDataProtectionProvider(), clock);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        var pending = new PendingWebPasswordLogin(new string('x', 43),
            clock.Now.AddMinutes(3), "private@fullworth.local", true, true);
        service.Write(context, pending);
        var header = context.Response.Headers.SetCookie.Single()!;
        Assert.StartsWith("__Host-", header);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", header, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(pending.Email, header);
        Assert.DoesNotContain(pending.LoginId, header);
        context.Request.Headers.Cookie = header.Split(';')[0];
        Assert.Equal(pending, service.Read(context));
        context.Request.Headers.Cookie = PendingPasswordLoginCookie.Name + "=tampered";
        Assert.Null(service.Read(context));
        context.Request.Headers.Cookie = header.Split(';')[0];
        clock.Now += TimeSpan.FromMinutes(4);
        Assert.Null(service.Read(context));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}

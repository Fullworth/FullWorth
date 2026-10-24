using System.Net;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidLinkCreateFailureRecoveryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FailedLinkSessionSave_DoesNotLeakPendingSessionOnSubsequentSave(
        bool updateMode, bool cancel)
    {
        var interceptor = new FailNextLinkSessionSave();
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(interceptor).Options);
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var originalTime = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var ownerToken = protector.Protect("owner-access-token");
        var foreignToken = protector.Protect("foreign-access-token");
        var owned = new BankConnectionEntity
        {
            UserId = owner,
            InstitutionName = "Owned bank",
            ProtectedPlaidAccessToken = ownerToken,
            PlaidItemId = "owned-item",
            Status = BankConnectionStatus.RequiresAttention,
            TransactionsCursor = "retained-cursor",
            UpdatedAtUtc = originalTime
        };
        var foreign = new BankConnectionEntity
        {
            UserId = stranger,
            InstitutionName = "Foreign bank",
            ProtectedPlaidAccessToken = foreignToken,
            PlaidItemId = "foreign-item",
            Status = BankConnectionStatus.Active,
            UpdatedAtUtc = originalTime
        };
        db.BankConnections.AddRange(owned, foreign);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        owned = await db.BankConnections.SingleAsync(x => x.Id == owned.Id);
        owned.InstitutionName = "Unsaved caller edit";
        var connectionId = updateMode ? owned.Id : (Guid?)null;

        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse("first-provider-link"),
            JsonResponse("fresh-provider-link"));
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client",
            Secret = "fixture-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        var service = new PlaidLinkService(api, protector, db);

        using var canceled = new CancellationTokenSource();
        interceptor.Arm = true;
        interceptor.Cancel = cancel ? canceled.Cancel : null;

        var error = await Record.ExceptionAsync(() =>
            service.CreateLinkSessionAsync(owner, connectionId, canceled.Token));
        if (cancel)
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        else
            Assert.IsType<DbUpdateException>(error);
        Assert.Equal(1, interceptor.Failures);
        Assert.DoesNotContain(db.ChangeTracker.Entries<PlaidLinkSessionEntity>(),
            e => e.State == EntityState.Added);

        // Caller work can still be committed independently, but the failed
        // Link token is no longer tracked and must never be persisted.
        await db.SaveChangesAsync();
        Assert.Empty(await db.PlaidLinkSessions.AsNoTracking().ToListAsync());
        Assert.Equal("Unsaved caller edit",
            (await db.BankConnections.AsNoTracking()
                .SingleAsync(x => x.Id == owned.Id)).InstitutionName);
        Assert.Equal("retained-cursor",
            (await db.BankConnections.AsNoTracking()
                .SingleAsync(x => x.Id == owned.Id)).TransactionsCursor);

        // A fresh provider link token is a new attempt. The old token was
        // not returned to the client and is not silently revived.
        var fresh = await service.CreateLinkSessionAsync(owner, connectionId);
        var saved = await db.PlaidLinkSessions.AsNoTracking().SingleAsync();
        Assert.Equal(fresh.SessionId, saved.Id);
        Assert.Equal(owner, saved.UserId);
        Assert.Equal(connectionId, saved.BankConnectionId);
        Assert.Equal(PlaidLinkSessionStatus.Pending, saved.Status);
        Assert.Equal("fresh-provider-link",
            protector.UnprotectLinkToken(saved.ProtectedLinkToken));
        Assert.DoesNotContain("first-provider-link", saved.ProtectedLinkToken,
            StringComparison.Ordinal);

        var unchanged = await db.BankConnections.AsNoTracking()
            .SingleAsync(x => x.Id == foreign.Id);
        Assert.Equal(stranger, unchanged.UserId);
        Assert.Equal("Foreign bank", unchanged.InstitutionName);
        Assert.Equal(foreignToken, unchanged.ProtectedPlaidAccessToken);
        Assert.Equal(originalTime, unchanged.UpdatedAtUtc);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests,
            request => Assert.Equal("/link/token/create", request.Uri.AbsolutePath));
    }

    private static HttpResponseMessage JsonResponse(string token) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                $$"""
                {"link_token":"{{token}}",
                 "hosted_link_url":"https://secure.plaid.com/sandbox",
                 "expiration":"2099-08-30T18:00:00Z"}
                """,
                Encoding.UTF8, "application/json")
        };

    private sealed class FailNextLinkSessionSave : SaveChangesInterceptor
    {
        public bool Arm { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Arm || !eventData.Context!.ChangeTracker
                    .Entries<PlaidLinkSessionEntity>()
                    .Any(entry => entry.State == EntityState.Added))
                return ValueTask.FromResult(result);

            Arm = false;
            Failures++;
            if (Cancel is not null)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new DbUpdateException("Synthetic Hosted Link session insert failure.");
        }
    }
}

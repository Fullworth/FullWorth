using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidHostedCompletionReplayOwnershipTests
{
    [Theory]
    [InlineData("owned-active")]
    [InlineData("owned-attention")]
    [InlineData("owned-disconnected")]
    [InlineData("foreign")]
    public async Task CompletedSessionReplay_UsesFreshOwnerScopedBankEvidence(
        string mode)
    {
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var protector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var protectedOwnerCredential = protector.Protect("owner-private-credential");
        var protectedForeignCredential = protector.Protect("other-private-credential");

        var owned = new BankConnectionEntity
        {
            UserId = owner,
            PlaidItemId = "owned-item",
            InstitutionName = "Owned institution",
            ProtectedPlaidAccessToken = protectedOwnerCredential,
            Status = mode == "owned-disconnected"
                ? BankConnectionStatus.Disconnected
                : mode == "owned-attention"
                    ? BankConnectionStatus.RequiresAttention
                    : BankConnectionStatus.Active,
            TransactionsCursor = "owner-retained-cursor"
        };
        var foreign = new BankConnectionEntity
        {
            UserId = otherOwner,
            PlaidItemId = "foreign-item",
            InstitutionName = "Foreign institution",
            ProtectedPlaidAccessToken = protectedForeignCredential,
            Status = BankConnectionStatus.Active
        };
        var session = new PlaidLinkSessionEntity
        {
            UserId = owner,
            BankConnectionId = mode == "foreign" ? foreign.Id : owned.Id,
            Status = PlaidLinkSessionStatus.Completed,
            CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(5),
            ProtectedLinkToken = string.Empty
        };
        db.BankConnections.AddRange(owned, foreign);
        db.PlaidLinkSessions.Add(session);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        using var handler = new ScriptedHttpMessageHandler();
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "test-client", Secret = "test-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        var service = new PlaidHostedLinkCompletionService(
            db, api, protector,
            new PlaidConnectionExchangeService(api, protector, db));

        var result = await service.CheckAndCompleteAsync(owner, session.Id);
        Assert.Equal(session.Id, result.SessionId);
        Assert.Equal("Completed", result.Status);

        if (mode is "owned-active" or "owned-attention")
        {
            Assert.NotNull(result.Connection);
            Assert.Equal(owned.Id, result.Connection.Id);
            Assert.Equal("Owned institution", result.Connection.InstitutionName);
            Assert.Equal(mode == "owned-active" ? "Active" : "RequiresAttention",
                result.Connection.Status);
        }
        else
        {
            // No false connected result from an archived bank or another owner.
            Assert.Null(result.Connection);
        }

        Assert.Empty(handler.Requests);
        var retainedOwner = await db.BankConnections.AsNoTracking()
            .SingleAsync(x => x.Id == owned.Id);
        var retainedForeign = await db.BankConnections.AsNoTracking()
            .SingleAsync(x => x.Id == foreign.Id);
        Assert.Equal(protectedOwnerCredential, retainedOwner.ProtectedPlaidAccessToken);
        Assert.Equal("owner-retained-cursor", retainedOwner.TransactionsCursor);
        Assert.Equal(protectedForeignCredential, retainedForeign.ProtectedPlaidAccessToken);
    }

    [Fact]
    public async Task CompletedSessionReplay_CrossOwnerSessionIdIsNotFound()
    {
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var session = new PlaidLinkSessionEntity
        {
            UserId = owner,
            Status = PlaidLinkSessionStatus.Completed,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.PlaidLinkSessions.Add(session);
        await db.SaveChangesAsync();

        var protector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        using var handler = new ScriptedHttpMessageHandler();
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "test-client", Secret = "test-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        var service = new PlaidHostedLinkCompletionService(
            db, api, protector,
            new PlaidConnectionExchangeService(api, protector, db));

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.CheckAndCompleteAsync(stranger, session.Id));
        Assert.Empty(handler.Requests);
    }
}

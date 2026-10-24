using System.Net;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidDisconnectFailureRecoveryTests
{
    private static readonly DateTimeOffset OriginalTime =
        DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    [Theory]
    [InlineData(BankConnectionStatus.Active, false)]
    [InlineData(BankConnectionStatus.Active, true)]
    [InlineData(BankConnectionStatus.RequiresAttention, false)]
    [InlineData(BankConnectionStatus.RequiresAttention, true)]
    [InlineData(BankConnectionStatus.Disconnected, false)]
    [InlineData(BankConnectionStatus.Disconnected, true)]
    public async Task FailedSaveRestoresDisconnectState_PreservesCallerEdits_AndAllowsRetry(
        BankConnectionStatus status, bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new FailNextSave(cancellation, cancel);
        await using var database = CreateDatabase(failure);
        var fixture = await Seed(database, status);
        database.ChangeTracker.Clear();
        var connection = await database.BankConnections.SingleAsync(c => c.Id == fixture.Connection.Id);
        var account = await database.BankAccounts.SingleAsync(a => a.Id == fixture.Account.Id);
        var unrelated = await database.BankConnections.SingleAsync(c => c.Id == fixture.ForeignConnection.Id);

        connection.IsArchived = true;
        account.Name = "Caller renamed account";
        unrelated.InstitutionName = "Caller unrelated edit";
        database.ChangeTracker.DetectChanges();
        var originalCredential = connection.ProtectedPlaidAccessToken;
        using var handler = ProviderResponses();
        using var http = new HttpClient(handler);
        var service = CreateService(database, http, fixture.Protector);
        failure.Armed = true;

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.DisconnectAsync(fixture.UserId, connection.Id, cancellation.Token));
        else
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.DisconnectAsync(fixture.UserId, connection.Id, cancellation.Token));

        Assert.Equal(status, connection.Status);
        Assert.Equal(originalCredential, connection.ProtectedPlaidAccessToken);
        Assert.Equal("cursor-original", connection.TransactionsCursor);
        Assert.Equal(OriginalTime, connection.UpdatedAtUtc);
        Assert.Equal(OriginalTime, connection.LastSuccessfulSyncAtUtc);
        Assert.True(account.IsActive);
        Assert.Equal(OriginalTime, account.UpdatedAtUtc);
        Assert.True(connection.IsArchived);
        Assert.Equal("Caller renamed account", account.Name);
        Assert.Equal("Caller unrelated edit", unrelated.InstitutionName);

        // Restore EF originals/dirty flags as well as values: a subsequent save
        // may commit caller work, but must not commit any failed disconnect.
        Assert.Equal(EntityState.Modified, database.Entry(connection).State);
        Assert.True(database.Entry(connection).Property(c => c.IsArchived).IsModified);
        Assert.False(database.Entry(connection).Property(c => c.Status).IsModified);
        Assert.False(database.Entry(connection).Property(c => c.ProtectedPlaidAccessToken).IsModified);
        Assert.False(database.Entry(connection).Property(c => c.TransactionsCursor).IsModified);
        Assert.False(database.Entry(connection).Property(c => c.UpdatedAtUtc).IsModified);
        Assert.False(database.Entry(connection).OriginalValues.GetValue<bool>(nameof(BankConnectionEntity.IsArchived)));
        Assert.True(database.Entry(account).Property(a => a.Name).IsModified);
        Assert.False(database.Entry(account).Property(a => a.IsActive).IsModified);
        Assert.Equal(fixture.Account.Name,
            database.Entry(account).OriginalValues.GetValue<string>(nameof(BankAccountEntity.Name)));

        await database.SaveChangesAsync();
        await AssertStoredState(database, fixture, status, originalCredential);
        Assert.True((await database.BankConnections.AsNoTracking().SingleAsync(c => c.Id == connection.Id)).IsArchived);
        Assert.Equal("Caller renamed account",
            (await database.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == account.Id)).Name);
        Assert.Equal("Caller unrelated edit",
            (await database.BankConnections.AsNoTracking().SingleAsync(c => c.Id == unrelated.Id)).InstitutionName);

        // The provider revoked the Item before the local failure. Retry sends
        // the retained credential, accepts ITEM_NOT_FOUND and commits once.
        Assert.True(await service.DisconnectAsync(fixture.UserId, connection.Id));
        await AssertSuccessfulRetry(database, fixture, handler);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAccountLookupDoesNotStageCredentialRemoval_AndAllowsRetry(bool cancel)
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new FailAccountMaterialization(cancellation, cancel);
        await using var database = CreateDatabase(failure);
        var fixture = await Seed(database, BankConnectionStatus.Active);
        database.ChangeTracker.Clear();
        // Do not preload accounts: exercise failure while materializing the
        // post-revocation owner-scoped account query.
        var connection = await database.BankConnections.SingleAsync(c => c.Id == fixture.Connection.Id);
        connection.IsArchived = true;
        var originalCredential = connection.ProtectedPlaidAccessToken;
        using var handler = ProviderResponses();
        using var http = new HttpClient(handler);
        var service = CreateService(database, http, fixture.Protector);
        failure.Armed = true;

        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                service.DisconnectAsync(fixture.UserId, connection.Id, cancellation.Token));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.DisconnectAsync(fixture.UserId, connection.Id, cancellation.Token));

        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Equal(originalCredential, connection.ProtectedPlaidAccessToken);
        Assert.Equal("cursor-original", connection.TransactionsCursor);
        Assert.Equal(OriginalTime, connection.UpdatedAtUtc);
        Assert.True(connection.IsArchived);
        await database.SaveChangesAsync();
        await AssertStoredState(database, fixture, BankConnectionStatus.Active, originalCredential);
        Assert.True(await service.DisconnectAsync(fixture.UserId, connection.Id));
        await AssertSuccessfulRetry(database, fixture, handler);
    }

    private static async Task AssertStoredState(FullWorthDbContext database, Fixture fixture,
        BankConnectionStatus status, string? credential)
    {
        var connection = await database.BankConnections.AsNoTracking().SingleAsync(c => c.Id == fixture.Connection.Id);
        Assert.Equal(status, connection.Status);
        Assert.Equal(credential, connection.ProtectedPlaidAccessToken);
        Assert.Equal("cursor-original", connection.TransactionsCursor);
        Assert.Equal(OriginalTime, connection.UpdatedAtUtc);
        Assert.Equal(OriginalTime, connection.LastSuccessfulSyncAtUtc);
        var accounts = await database.BankAccounts.AsNoTracking()
            .Where(a => a.BankConnectionId == connection.Id).ToListAsync();
        Assert.Equal(2, accounts.Count);
        Assert.True(accounts.Single(a => a.Id == fixture.Account.Id).IsActive);
        Assert.False(accounts.Single(a => a.Id != fixture.Account.Id).IsActive);
        Assert.All(accounts, a => Assert.Equal(OriginalTime, a.UpdatedAtUtc));
        await AssertForeignAndHistoryUntouched(database, fixture);
    }

    private static async Task AssertSuccessfulRetry(FullWorthDbContext database, Fixture fixture,
        ScriptedHttpMessageHandler handler)
    {
        var connection = await database.BankConnections.AsNoTracking().SingleAsync(c => c.Id == fixture.Connection.Id);
        Assert.Equal(BankConnectionStatus.Disconnected, connection.Status);
        Assert.Null(connection.ProtectedPlaidAccessToken);
        Assert.Null(connection.TransactionsCursor);
        Assert.All(await database.BankAccounts.AsNoTracking()
            .Where(a => a.BankConnectionId == connection.Id).ToListAsync(), a => Assert.False(a.IsActive));
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal("/item/remove", request.Uri.AbsolutePath);
            using var body = JsonDocument.Parse(request.Body);
            Assert.Equal("fixture-revoked-token", body.RootElement.GetProperty("access_token").GetString());
        });
        await AssertForeignAndHistoryUntouched(database, fixture);
    }

    private static async Task AssertForeignAndHistoryUntouched(FullWorthDbContext database, Fixture fixture)
    {
        var foreign = await database.BankConnections.AsNoTracking().SingleAsync(c => c.Id == fixture.ForeignConnection.Id);
        Assert.Equal(BankConnectionStatus.Active, foreign.Status);
        Assert.Equal(fixture.ForeignConnection.ProtectedPlaidAccessToken, foreign.ProtectedPlaidAccessToken);
        Assert.Equal("cursor-original", foreign.TransactionsCursor);
        Assert.Equal(OriginalTime, foreign.UpdatedAtUtc);
        Assert.True((await database.BankAccounts.AsNoTracking().SingleAsync(a => a.Id == fixture.ForeignAccount.Id)).IsActive);
        var history = await database.BankTransactions.AsNoTracking().OrderBy(t => t.Amount).ToListAsync();
        Assert.Equal(new[] { 11.23m, 99.50m }, history.Select(t => t.Amount).ToArray());
        Assert.All(history, t => Assert.False(t.IsRemoved));
    }

    private static FullWorthDbContext CreateDatabase(IInterceptor interceptor) => new(
        new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).AddInterceptors(interceptor).Options);

    private static PlaidConnectionDisconnectService CreateService(FullWorthDbContext database,
        HttpClient http, PlaidTokenProtector protector) => new(database,
        new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client", Secret = "fixture-secret", Environment = PlaidOptions.SandboxEnvironment
        })), protector);

    private static ScriptedHttpMessageHandler ProviderResponses() => new(
        JsonResponse(HttpStatusCode.OK, "{}"),
        JsonResponse(HttpStatusCode.BadRequest,
            "{\"error_type\":\"INVALID_INPUT\",\"error_code\":\"ITEM_NOT_FOUND\"}"));

    private static HttpResponseMessage JsonResponse(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<Fixture> Seed(FullWorthDbContext database, BankConnectionStatus status)
    {
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var userId = Guid.NewGuid();
        var foreignUser = Guid.NewGuid();
        BankConnectionEntity Connection(Guid owner, string token, BankConnectionStatus state) => new()
        {
            UserId = owner, InstitutionName = "Fixture bank", PlaidItemId = Guid.NewGuid().ToString(),
            ProtectedPlaidAccessToken = protector.Protect(token), Status = state,
            TransactionsCursor = "cursor-original", UpdatedAtUtc = OriginalTime,
            LastSuccessfulSyncAtUtc = OriginalTime
        };
        BankAccountEntity Account(BankConnectionEntity bank, bool active = true) => new()
        {
            UserId = bank.UserId, BankConnectionId = bank.Id, PlaidAccountId = Guid.NewGuid().ToString(),
            Name = "Original account", IsActive = active, UpdatedAtUtc = OriginalTime
        };
        var connection = Connection(userId, "fixture-revoked-token", status);
        var foreign = Connection(foreignUser, "fixture-foreign-token", BankConnectionStatus.Active);
        var account = Account(connection);
        var inactive = Account(connection, false);
        var foreignAccount = Account(foreign);
        database.BankConnections.AddRange(connection, foreign);
        database.BankAccounts.AddRange(account, inactive, foreignAccount);
        foreach (var (bankAccount, amount) in new[] { (account, 11.23m), (foreignAccount, 99.50m) })
            database.BankTransactions.Add(new BankTransactionEntity
            {
                UserId = bankAccount.UserId, BankAccountId = bankAccount.Id,
                PlaidTransactionId = Guid.NewGuid().ToString(), Name = "Historical charge",
                Amount = amount, PostedDate = new DateOnly(2026, 8, 1), IsoCurrencyCode = "USD"
            });
        await database.SaveChangesAsync();
        return new Fixture(userId, connection, account, foreign, foreignAccount, protector);
    }

    private sealed record Fixture(Guid UserId, BankConnectionEntity Connection, BankAccountEntity Account,
        BankConnectionEntity ForeignConnection, BankAccountEntity ForeignAccount, PlaidTokenProtector Protector);

    private sealed class FailNextSave(CancellationTokenSource caller, bool cancel) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Armed = false;
            if (cancel) { caller.Cancel(); throw new OperationCanceledException(cancellationToken); }
            throw new DbUpdateException("Synthetic pre-commit disconnect save failure.");
        }
    }

    private sealed class FailAccountMaterialization(CancellationTokenSource caller, bool cancel) : IMaterializationInterceptor
    {
        public bool Armed { get; set; }
        public object InitializedInstance(MaterializationInterceptionData data, object entity)
        {
            if (!Armed || entity is not BankAccountEntity) return entity;
            Armed = false;
            if (cancel) { caller.Cancel(); throw new OperationCanceledException(caller.Token); }
            throw new InvalidOperationException("Synthetic account lookup failure.");
        }
    }
}

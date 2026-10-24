using System.Net;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidAttentionFailureRecoveryTests
{
    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var transactions in new[] { false, true })
        foreach (var bulk in new[] { false, true })
        foreach (var cancel in new[] { false, true })
        foreach (var callerTimestampEdit in new[] { false, true })
            yield return [transactions, bulk, cancel, callerTimestampEdit];
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task FailedAttentionSave_RestoresCallerState_AndRetryPersistsAndLogs(
        bool transactions, bool bulk, bool cancel, bool callerTimestampEdit)
    {
        var failure = new FailNextSave();
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(failure).Options);
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var owner = Guid.NewGuid();
        var oldUpdated = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var oldSync = oldUpdated.AddDays(-1);
        var connection = new BankConnectionEntity
        {
            UserId = owner, InstitutionName = "Original bank",
            PlaidItemId = "fixture-item",
            ProtectedPlaidAccessToken = protector.Protect("fixture-owner-token"),
            TransactionsCursor = "original-cursor",
            Status = BankConnectionStatus.Active,
            UpdatedAtUtc = oldUpdated, LastSuccessfulSyncAtUtc = oldSync
        };
        var foreign = new BankConnectionEntity
        {
            UserId = Guid.NewGuid(), InstitutionName = "Foreign bank",
            ProtectedPlaidAccessToken = protector.Protect("fixture-foreign-token"),
            UpdatedAtUtc = oldUpdated
        };
        var account = new BankAccountEntity
        {
            UserId = owner, BankConnectionId = connection.Id,
            PlaidAccountId = "fixture-account", Name = "Original account", IsActive = true
        };
        db.AddRange(connection, foreign, account);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        connection = await db.BankConnections.SingleAsync(c => c.Id == connection.Id);
        account = await db.BankAccounts.SingleAsync(a => a.Id == account.Id);
        connection.InstitutionName = "Unsaved caller bank edit";
        account.Name = "Unsaved caller account edit";
        var expectedUpdated = callerTimestampEdit ? oldUpdated.AddDays(1) : oldUpdated;
        if (callerTimestampEdit) connection.UpdatedAtUtc = expectedUpdated;
        db.ChangeTracker.DetectChanges();
        var entry = db.Entry(connection);
        var modifiedBefore = entry.Properties.Where(p => p.IsModified)
            .Select(p => p.Metadata.Name).Order().ToArray();

        using var handler = new ScriptedHttpMessageHandler(Error(), Error());
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client", Secret = "fixture-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        using var logger = new EventLoggerFactory();
        var coordinator = new PlaidConnectionSyncCoordinator(db,
            new PlaidAccountSyncService(db, api, protector),
            new PlaidTransactionSyncService(db, api, protector),
            logger, TestSecurityAlertAggregator.Create());
        using var cancellation = new CancellationTokenSource();
        failure.Armed = true;
        failure.Cancel = cancel ? cancellation.Cancel : null;

        var error = await Record.ExceptionAsync(() =>
            Sync(coordinator, transactions, bulk, owner, connection.Id, cancellation.Token));
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<DbUpdateException>(error);
        Assert.Equal(1, failure.Failures);
        Assert.Empty(logger.Events);
        Assert.Equal(BankConnectionStatus.Active, connection.Status);
        Assert.Equal(expectedUpdated, connection.UpdatedAtUtc);
        Assert.Equal(EntityState.Modified, entry.State);
        Assert.Equal(modifiedBefore, entry.Properties.Where(p => p.IsModified)
            .Select(p => p.Metadata.Name).Order().ToArray());
        Assert.Equal(BankConnectionStatus.Active, entry.OriginalValues.GetValue<BankConnectionStatus>(nameof(connection.Status)));
        Assert.Equal(oldUpdated, entry.OriginalValues.GetValue<DateTimeOffset>(nameof(connection.UpdatedAtUtc)));
        Assert.Equal("original-cursor", connection.TransactionsCursor);
        Assert.Equal(oldSync, connection.LastSuccessfulSyncAtUtc);
        Assert.Equal("fixture-owner-token", protector.Unprotect(connection.ProtectedPlaidAccessToken!));
        Assert.Empty(db.ChangeTracker.Entries<BankTransactionEntity>());

        // Another operation reuses this context. Only the caller's edits may
        // persist; neither the failed attention status nor its timestamp may leak.
        await db.SaveChangesAsync();
        var stored = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(BankConnectionStatus.Active, stored.Status);
        Assert.Equal(expectedUpdated, stored.UpdatedAtUtc);
        Assert.Equal("Unsaved caller bank edit", stored.InstitutionName);
        Assert.Equal("Unsaved caller account edit",
            (await db.BankAccounts.AsNoTracking().SingleAsync()).Name);
        Assert.Equal(oldSync, stored.LastSuccessfulSyncAtUtc);
        Assert.Equal("original-cursor", stored.TransactionsCursor);

        // Retry in the same context must persist attention before propagating
        // the provider error, and emit the security event exactly once.
        var providerError = await Assert.ThrowsAsync<PlaidApiException>(() =>
            Sync(coordinator, transactions, bulk, owner, connection.Id));
        Assert.Equal("ITEM_LOGIN_REQUIRED", providerError.ErrorCode);
        stored = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(BankConnectionStatus.RequiresAttention, stored.Status);
        Assert.True(stored.UpdatedAtUtc > expectedUpdated);
        Assert.Equal(oldSync, stored.LastSuccessfulSyncAtUtc);
        Assert.Equal("original-cursor", stored.TransactionsCursor);
        Assert.Equal("fixture-owner-token", protector.Unprotect(stored.ProtectedPlaidAccessToken!));
        Assert.Equal(29014, Assert.Single(logger.Events).Id);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Contains("fixture-owner-token", request.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("fixture-foreign-token", request.Body, StringComparison.Ordinal);
        });
        var storedForeign = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == foreign.Id);
        Assert.Equal(BankConnectionStatus.Active, storedForeign.Status);
        Assert.Equal(oldUpdated, storedForeign.UpdatedAtUtc);
        Assert.False(await db.BankTransactions.AnyAsync());
    }

    private static Task Sync(PlaidConnectionSyncCoordinator coordinator,
        bool transactions, bool bulk, Guid owner, Guid connection,
        CancellationToken token = default) =>
        (transactions, bulk) switch
        {
            (false, false) => coordinator.SyncAccountsAsync(owner, connection, token),
            (true, false) => coordinator.SyncTransactionsAsync(owner, connection, token),
            (false, true) => coordinator.SyncAllAccountsAsync(owner, token),
            (true, true) => coordinator.SyncAllTransactionsAsync(owner, token)
        };

    private static HttpResponseMessage Error() => new(HttpStatusCode.BadRequest)
    {
        Content = new StringContent(
            """{"error_type":"ITEM_ERROR","error_code":"ITEM_LOGIN_REQUIRED"}""",
            Encoding.UTF8, "application/json")
    };

    private sealed class FailNextSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Failures++;
                Assert.Contains(eventData.Context!.ChangeTracker.Entries<BankConnectionEntity>(),
                    e => e.Entity.Status == BankConnectionStatus.RequiresAttention);
                if (Cancel is not null)
                {
                    Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                throw new DbUpdateException("Synthetic pre-commit attention save failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class EventLoggerFactory : ILoggerFactory, ILogger
    {
        public List<EventId> Events { get; } = [];
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) { }
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter) => Events.Add(eventId);
    }
}

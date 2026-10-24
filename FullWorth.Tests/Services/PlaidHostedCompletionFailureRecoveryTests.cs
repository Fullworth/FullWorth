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

public sealed class PlaidHostedCompletionFailureRecoveryTests
{
    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var mode in new[] { "update", "expired", "exited", "failed", "new" })
        foreach (var cancel in new[] { false, true })
        foreach (var callerTimestampEdit in new[] { false, true })
            yield return [mode, cancel, callerTimestampEdit];
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task FailedCompletion_RestoresPendingSession_WithoutLeakingTerminalState(
        string mode, bool cancel, bool callerTimestampEdit)
    {
        var failure = new FailTerminalSave();
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .AddInterceptors(failure).Options);
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var owner = Guid.NewGuid();
        var oldUpdated = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var connection = new BankConnectionEntity
        {
            UserId = owner, InstitutionName = "Original bank",
            PlaidItemId = "fixture-original-item",
            ProtectedPlaidAccessToken = protector.Protect("fixture-access-token"),
            TransactionsCursor = "original-cursor",
            Status = BankConnectionStatus.RequiresAttention,
            UpdatedAtUtc = oldUpdated, LastSuccessfulSyncAtUtc = oldUpdated.AddDays(-1)
        };
        var session = new PlaidLinkSessionEntity
        {
            UserId = owner,
            BankConnectionId = mode == "update" ? connection.Id : null,
            ProtectedLinkToken = protector.ProtectLinkToken("fixture-link-token"),
            ExpiresAtUtc = mode == "expired" ? DateTimeOffset.UtcNow.AddMinutes(-5) : DateTimeOffset.UtcNow.AddHours(1),
            UpdatedAtUtc = oldUpdated
        };
        var foreign = new PlaidLinkSessionEntity
        {
            UserId = Guid.NewGuid(),
            ProtectedLinkToken = protector.ProtectLinkToken("fixture-foreign-token"),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1), UpdatedAtUtc = oldUpdated
        };
        db.AddRange(connection, session, foreign);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        connection = await db.BankConnections.SingleAsync();
        session = await db.PlaidLinkSessions.SingleAsync(s => s.Id == session.Id);
        var protectedLink = session.ProtectedLinkToken;
        connection.InstitutionName = "Unsaved caller bank edit";
        var expectedUpdated = callerTimestampEdit ? oldUpdated.AddDays(1) : oldUpdated;
        if (callerTimestampEdit) session.UpdatedAtUtc = expectedUpdated;
        db.ChangeTracker.DetectChanges();
        var sessionEntry = db.Entry(session);
        var sessionState = sessionEntry.State;
        var modifiedBefore = Modified(sessionEntry);
        var connectionEntry = db.Entry(connection);
        var connectionModifiedBefore = Modified(connectionEntry);

        using var handler = mode == "new"
            ? new ScriptedHttpMessageHandler(
                Status(mode),
                Json("""{"access_token":"fixture-new-access","item_id":"fixture-new-item"}"""),
                Json("""{"item":{"item_id":"fixture-new-item","institution_name":"New fixture bank"}}"""))
            : new ScriptedHttpMessageHandler(Status(mode), Status(mode));
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client", Secret = "fixture-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        var service = new PlaidHostedLinkCompletionService(db, api, protector,
            new PlaidConnectionExchangeService(api, protector, db));
        using var cancellation = new CancellationTokenSource();
        failure.Armed = true;
        failure.Cancel = cancel ? cancellation.Cancel : null;

        var error = await Record.ExceptionAsync(() =>
            service.CheckAndCompleteAsync(owner, session.Id, cancellation.Token));
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<DbUpdateException>(error);
        Assert.Equal(1, failure.Failures);
        Assert.Equal(PlaidLinkSessionStatus.Pending, session.Status);
        Assert.Null(session.CompletedAtUtc);
        Assert.Equal(protectedLink, session.ProtectedLinkToken);
        Assert.Equal(expectedUpdated, session.UpdatedAtUtc);
        Assert.Equal(sessionState, sessionEntry.State);
        Assert.Equal(modifiedBefore, Modified(sessionEntry));
        Assert.Equal(PlaidLinkSessionStatus.Pending,
            sessionEntry.OriginalValues.GetValue<PlaidLinkSessionStatus>(nameof(session.Status)));
        Assert.Equal(oldUpdated,
            sessionEntry.OriginalValues.GetValue<DateTimeOffset>(nameof(session.UpdatedAtUtc)));
        Assert.Equal(BankConnectionStatus.RequiresAttention, connection.Status);
        Assert.Equal(oldUpdated, connection.UpdatedAtUtc);
        if (mode != "new") Assert.Equal(connectionModifiedBefore, Modified(connectionEntry));

        // An unrelated operation must not persist the failed completion or
        // credential clearing, and must still be able to save caller edits.
        await db.SaveChangesAsync();
        var storedSession = await db.PlaidLinkSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.Equal(PlaidLinkSessionStatus.Pending, storedSession.Status);
        Assert.Null(storedSession.CompletedAtUtc);
        Assert.Equal(protectedLink, storedSession.ProtectedLinkToken);
        Assert.Equal(expectedUpdated, storedSession.UpdatedAtUtc);
        var storedConnection = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(BankConnectionStatus.RequiresAttention, storedConnection.Status);
        Assert.Equal("Unsaved caller bank edit", storedConnection.InstitutionName);
        Assert.Equal(oldUpdated, storedConnection.UpdatedAtUtc);
        Assert.Equal("original-cursor", storedConnection.TransactionsCursor);
        Assert.Equal(oldUpdated.AddDays(-1), storedConnection.LastSuccessfulSyncAtUtc);
        Assert.Equal("fixture-access-token", protector.Unprotect(storedConnection.ProtectedPlaidAccessToken!));
        var storedForeign = await db.PlaidLinkSessions.AsNoTracking().SingleAsync(s => s.Id == foreign.Id);
        Assert.Equal(PlaidLinkSessionStatus.Pending, storedForeign.Status);
        Assert.Equal(oldUpdated, storedForeign.UpdatedAtUtc);

        if (mode == "new")
        {
            // One database save stages both the bank and terminal session.
            // A pre-commit error must not leave a bank connected to a Pending
            // Hosted Link session (and must not persist the failed candidate
            // through this context's subsequent unrelated caller save).
            Assert.Single(await db.BankConnections.ToListAsync());
            Assert.False(await db.BankConnections.AnyAsync(c =>
                c.UserId == owner && c.PlaidItemId == "fixture-new-item"));
            Assert.Null(session.BankConnectionId);
            Assert.Null(storedSession.BankConnectionId);
            Assert.Equal(3, handler.Requests.Count);
            // This does NOT make the provider's already-consumed public token
            // replayable after failure. A new Link attempt may be required.
            return;
        }

        var result = await service.CheckAndCompleteAsync(owner, session.Id);
        var expectedStatus = mode switch
        {
            "update" => PlaidLinkSessionStatus.Completed,
            "expired" => PlaidLinkSessionStatus.Expired,
            "exited" => PlaidLinkSessionStatus.Exited,
            _ => PlaidLinkSessionStatus.Failed
        };
        Assert.Equal(expectedStatus.ToString(), result.Status);
        storedSession = await db.PlaidLinkSessions.AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.Equal(expectedStatus, storedSession.Status);
        Assert.NotNull(storedSession.CompletedAtUtc);
        Assert.Equal(string.Empty, storedSession.ProtectedLinkToken);
        storedConnection = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == connection.Id);
        Assert.Equal(mode == "update" ? BankConnectionStatus.Active : BankConnectionStatus.RequiresAttention,
            storedConnection.Status);
        Assert.Equal(oldUpdated.AddDays(-1), storedConnection.LastSuccessfulSyncAtUtc);
        Assert.Equal("original-cursor", storedConnection.TransactionsCursor);
        Assert.Equal(mode == "expired" ? 0 : 2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("/link/token/get", request.Uri.AbsolutePath));

        var requestCount = handler.Requests.Count;
        var terminalReplay = await service.CheckAndCompleteAsync(owner, session.Id);
        Assert.Equal(expectedStatus.ToString(), terminalReplay.Status);
        Assert.Equal(requestCount, handler.Requests.Count);
    }

    private static string[] Modified(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray();

    private static HttpResponseMessage Status(string mode) => Json(mode switch
    {
        "new" => """{"link_sessions":[{"finished_at":"2026-08-30T00:00:00Z","results":{"item_add_results":[{"public_token":"fixture-public-token"}]}}]}""",
        "exited" => """{"link_sessions":[{"finished_at":"2026-08-30T00:00:00Z","exit":{}}]}""",
        _ => """{"link_sessions":[{"finished_at":"2026-08-30T00:00:00Z","results":{}}]}"""
    });

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class FailTerminalSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<PlaidLinkSessionEntity>()
                .Any(e => e.Entity.Status != PlaidLinkSessionStatus.Pending))
            {
                Armed = false;
                Failures++;
                if (Cancel is not null)
                {
                    Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                throw new DbUpdateException("Synthetic pre-commit completion failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

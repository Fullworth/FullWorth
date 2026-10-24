using System.Net;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidExchangeFailureRecoveryTests
{
    private static readonly DateTimeOffset OldTime = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    public static IEnumerable<object[]> FailureCases()
    {
        foreach (var mode in new[] { "existing", "new", "race" })
        foreach (var cancel in new[] { false, true })
        foreach (var callerEdit in new[] { false, true })
            yield return [mode, cancel, callerEdit];
    }

    [Theory]
    [MemberData(nameof(FailureCases))]
    public async Task FailedExchangeSave_DiscardsOnlyExchangeState_AndAllowsFreshLinkRetry(
        string mode, bool cancel, bool callerEdit)
    {
        var name = Guid.NewGuid().ToString("N");
        var root = new InMemoryDatabaseRoot();
        var plainOptions = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(name, root).Options;
        var failure = new ExchangeFailure { PlainOptions = plainOptions };
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(name, root).AddInterceptors(failure).Options);
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var owner = Guid.NewGuid();
        var oldCredential = protector.Protect("fixture-old-access");
        BankConnectionEntity Connection(Guid user, string item, string bank) => new()
        {
            UserId = user, PlaidItemId = item, InstitutionName = bank,
            PlaidInstitutionId = "old-institution", ProtectedPlaidAccessToken = oldCredential,
            Status = BankConnectionStatus.RequiresAttention, TransactionsCursor = "old-cursor",
            LastSuccessfulSyncAtUtc = OldTime.AddDays(-1), UpdatedAtUtc = OldTime
        };
        var caller = Connection(owner, "caller-item", "Original caller bank");
        var foreign = Connection(Guid.NewGuid(), "target-item", "Foreign bank");
        var existing = mode == "existing" ? Connection(owner, "target-item", "Original target bank") : null;
        db.AddRange(caller, foreign);
        if (existing is not null) db.Add(existing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        caller = await db.BankConnections.SingleAsync(c => c.Id == caller.Id);
        caller.InstitutionName = "Unsaved caller bank";
        string[] targetFlags = [];
        if (existing is not null)
        {
            existing = await db.BankConnections.SingleAsync(c => c.Id == existing.Id);
            if (callerEdit)
            {
                existing.InstitutionName = "Unsaved target bank";
                existing.UpdatedAtUtc = OldTime.AddDays(1);
            }
            db.ChangeTracker.DetectChanges();
            targetFlags = Modified(db.Entry(existing));
        }
        else if (callerEdit) caller.UpdatedAtUtc = OldTime.AddDays(1);

        failure.Winner = mode == "race" ? Connection(owner, "target-item", "Concurrent winner") : null;
        using var cancellation = new CancellationTokenSource();
        failure.Cancel = cancel ? cancellation.Cancel : null;
        failure.Armed = true;
        using var handler = new ScriptedHttpMessageHandler(Exchange(), Item(), Exchange(), Item());
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client", Secret = "fixture-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        var service = new PlaidConnectionExchangeService(api, protector, db);

        var error = await Record.ExceptionAsync(() =>
            service.ExchangeAndSaveAsync(owner, "fixture-public-first", cancellation.Token));
        if (cancel) Assert.IsAssignableFrom<OperationCanceledException>(error);
        else Assert.IsType<DbUpdateException>(error);
        Assert.Equal(mode == "race" ? 2 : 1, failure.Failures);
        Assert.Equal("Unsaved caller bank", caller.InstitutionName);
        Assert.DoesNotContain(db.ChangeTracker.Entries<BankConnectionEntity>(),
            e => e.State == EntityState.Added && e.Entity.UserId == owner && e.Entity.PlaidItemId == "target-item");
        if (existing is not null)
        {
            Assert.Equal(targetFlags, Modified(db.Entry(existing)));
            Assert.Equal(BankConnectionStatus.RequiresAttention, existing.Status);
            Assert.Equal(oldCredential, existing.ProtectedPlaidAccessToken);
            Assert.Equal("Original target bank",
                db.Entry(existing).OriginalValues.GetValue<string>(nameof(existing.InstitutionName)));
            Assert.Equal(OldTime,
                db.Entry(existing).OriginalValues.GetValue<DateTimeOffset>(nameof(existing.UpdatedAtUtc)));
        }

        // Reusing the context must preserve unrelated caller work without
        // committing a failed insert, credential replacement or reactivation.
        await db.SaveChangesAsync();
        var target = await db.BankConnections.AsNoTracking()
            .SingleOrDefaultAsync(c => c.UserId == owner && c.PlaidItemId == "target-item");
        if (mode == "new") Assert.Null(target);
        else
        {
            Assert.NotNull(target);
            Assert.Equal(BankConnectionStatus.RequiresAttention, target.Status);
            Assert.Equal(oldCredential, target.ProtectedPlaidAccessToken);
            Assert.Equal("old-institution", target.PlaidInstitutionId);
            Assert.Equal("old-cursor", target.TransactionsCursor);
            Assert.Equal(OldTime.AddDays(-1), target.LastSuccessfulSyncAtUtc);
            Assert.Equal(mode == "existing" && callerEdit ? OldTime.AddDays(1) : OldTime, target.UpdatedAtUtc);
            Assert.Equal(mode == "race" ? "Concurrent winner" :
                callerEdit ? "Unsaved target bank" : "Original target bank", target.InstitutionName);
        }
        var storedCaller = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == caller.Id);
        Assert.Equal("Unsaved caller bank", storedCaller.InstitutionName);
        Assert.Equal(mode != "existing" && callerEdit ? OldTime.AddDays(1) : OldTime, storedCaller.UpdatedAtUtc);

        // A fresh public token models a new Link attempt. This does not claim
        // that an already-consumed provider token can be replayed.
        var result = await service.ExchangeAndSaveAsync(owner, "fixture-public-fresh");
        target = await db.BankConnections.AsNoTracking().SingleAsync(c =>
            c.UserId == owner && c.PlaidItemId == "target-item");
        Assert.Equal(target.Id, result.Id);
        Assert.Equal(BankConnectionStatus.Active, target.Status);
        Assert.Equal("Provider bank", target.InstitutionName);
        Assert.Equal("fixture-new-access", protector.Unprotect(target.ProtectedPlaidAccessToken!));
        Assert.Equal(mode == "new" ? null : "old-cursor", target.TransactionsCursor);
        Assert.Equal(mode == "new" ? (DateTimeOffset?)null : OldTime.AddDays(-1), target.LastSuccessfulSyncAtUtc);
        Assert.Equal(1, await db.BankConnections.CountAsync(c => c.UserId == owner && c.PlaidItemId == "target-item"));
        var storedForeign = await db.BankConnections.AsNoTracking().SingleAsync(c => c.Id == foreign.Id);
        Assert.Equal("Foreign bank", storedForeign.InstitutionName);
        Assert.Equal(oldCredential, storedForeign.ProtectedPlaidAccessToken);
        Assert.Equal(BankConnectionStatus.RequiresAttention, storedForeign.Status);
        Assert.Equal(OldTime, storedForeign.UpdatedAtUtc);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Contains("fixture-public-first", handler.Requests[0].Body, StringComparison.Ordinal);
        Assert.Contains("fixture-public-fresh", handler.Requests[2].Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshTokenCannotReactivateDisconnectedItem_EvenAfterCompetingInsert(
        bool concurrentWinner)
    {
        var root = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString("N");
        var plainOptions = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(databaseName, root).Options;
        var failure = new ExchangeFailure { PlainOptions = plainOptions };
        await using var db = new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(databaseName, root)
                .AddInterceptors(failure).Options);

        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        var owner = Guid.NewGuid();
        var otherOwner = Guid.NewGuid();
        var originalCredential = protector.Protect("original-disconnected-credential");
        var foreignCredential = protector.Protect("unrelated-owner-credential");
        var disconnected = new BankConnectionEntity
        {
            UserId = owner,
            PlaidItemId = "shared-item-id",
            PlaidInstitutionId = "historical-institution",
            InstitutionName = "Disconnected institution",
            ProtectedPlaidAccessToken = originalCredential,
            Status = BankConnectionStatus.Disconnected,
            TransactionsCursor = "retained-disconnected-cursor",
            LastSuccessfulSyncAtUtc = OldTime.AddDays(-1),
            UpdatedAtUtc = OldTime
        };
        var foreign = new BankConnectionEntity
        {
            UserId = otherOwner,
            PlaidItemId = "shared-item-id",
            InstitutionName = "Unrelated owner institution",
            ProtectedPlaidAccessToken = foreignCredential,
            Status = BankConnectionStatus.Active,
            TransactionsCursor = "unrelated-owner-cursor",
            UpdatedAtUtc = OldTime
        };

        db.BankConnections.Add(foreign);
        if (!concurrentWinner)
            db.BankConnections.Add(disconnected);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        // The first write loses to an owner-scoped concurrent row. Recovery
        // must not bypass the disconnected lifecycle gate on the reloaded row.
        if (concurrentWinner)
        {
            failure.Winner = disconnected;
            failure.Armed = true;
        }

        using var handler = new ScriptedHttpMessageHandler(
            Json("""{"access_token":"fresh-provider-credential","item_id":"shared-item-id"}"""),
            Json("""{"item":{"item_id":"shared-item-id","institution_name":"New provider name"}}"""));
        using var http = new HttpClient(handler);
        var api = new PlaidApiClient(http, Options.Create(new PlaidOptions
        {
            ClientId = "fixture-client",
            Secret = "fixture-secret",
            Environment = PlaidOptions.SandboxEnvironment
        }));
        var service = new PlaidConnectionExchangeService(api, protector, db);

        var error = await Assert.ThrowsAsync<PlaidDisconnectedConnectionException>(
            () => service.ExchangeAndSaveAsync(owner, "new-public-token"));
        Assert.Contains("Disconnected bank connection", error.Message,
            StringComparison.Ordinal);
        failure.Armed = false;

        // Saving the reused context must not persist a candidate credential
        // or new connection after the exchange is rejected.
        await db.SaveChangesAsync();
        await using var verifier = new FullWorthDbContext(plainOptions);
        var retained = await verifier.BankConnections.AsNoTracking()
            .SingleAsync(c => c.UserId == owner && c.PlaidItemId == "shared-item-id");
        Assert.Equal(disconnected.Id, retained.Id);
        Assert.Equal(BankConnectionStatus.Disconnected, retained.Status);
        Assert.Equal("Disconnected institution", retained.InstitutionName);
        Assert.Equal("historical-institution", retained.PlaidInstitutionId);
        Assert.Equal(originalCredential, retained.ProtectedPlaidAccessToken);
        Assert.Equal("retained-disconnected-cursor", retained.TransactionsCursor);
        Assert.Equal(OldTime.AddDays(-1), retained.LastSuccessfulSyncAtUtc);
        Assert.Equal(OldTime, retained.UpdatedAtUtc);

        var untouchedForeign = await verifier.BankConnections.AsNoTracking()
            .SingleAsync(c => c.UserId == otherOwner && c.PlaidItemId == "shared-item-id");
        Assert.Equal(foreign.Id, untouchedForeign.Id);
        Assert.Equal(BankConnectionStatus.Active, untouchedForeign.Status);
        Assert.Equal(foreignCredential, untouchedForeign.ProtectedPlaidAccessToken);
        Assert.Equal("unrelated-owner-cursor", untouchedForeign.TransactionsCursor);
        Assert.Equal(2, await verifier.BankConnections.CountAsync());
        Assert.Collection(handler.Requests,
            r => Assert.Equal("/item/public_token/exchange", r.Uri.AbsolutePath),
            r => Assert.Equal("/item/get", r.Uri.AbsolutePath));
    }

    private static string[] Modified(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry entry) =>
        entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray();

    private static HttpResponseMessage Exchange() => Json(
        """{"access_token":"fixture-new-access","item_id":"target-item"}""");
    private static HttpResponseMessage Item() => Json(
        """{"item":{"item_id":"target-item","institution_id":"new-institution","institution_name":"Provider bank"}}""");
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(text, Encoding.UTF8, "application/json")
    };

    private sealed class ExchangeFailure : SaveChangesInterceptor
    {
        public required DbContextOptions<FullWorthDbContext> PlainOptions { get; init; }
        public BankConnectionEntity? Winner { get; set; }
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public int Failures { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Armed) return result;
            Failures++;
            if (Winner is not null && Failures == 1)
            {
                await using var winnerContext = new FullWorthDbContext(PlainOptions);
                winnerContext.Add(Winner);
                await winnerContext.SaveChangesAsync(CancellationToken.None);
                throw new DbUpdateException("Synthetic competing insert.");
            }
            Armed = false;
            if (Cancel is not null)
            {
                Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            throw new DbUpdateException("Synthetic pre-commit exchange failure.");
        }
    }
}

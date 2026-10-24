using System.Net;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FullWorth.Tests.Services;

public sealed class HostedLinkDurableReplayTests
{
    public static IEnumerable<object[]> TerminalCases()
    {
        foreach (var phase in new[] { "before-poll", "during-poll" })
        foreach (var mode in new[] { "initial", "update" })
        foreach (var terminal in new[] { "active", "attention", "disconnected",
                     "expired", "exited", "failed" })
            yield return [phase, mode, terminal];
    }

    [Theory]
    [MemberData(nameof(TerminalCases))]
    public async Task DurableTerminalState_WinsOverTrackedPending_WithoutReexchange(
        string phase, string mode, string terminal)
    {
        var fixture = await Fixture.CreateAsync(mode);
        await using var db = fixture.Open();
        var tracked = await db.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        var oldToken = tracked.ProtectedLinkToken;
        tracked.UpdatedAtUtc = Fixture.Now.AddMinutes(1);
        var callerBill = new BillStreamEntity { UserId = fixture.Owner, ProviderName = "Caller work" };
        db.Add(callerBill);

        async Task CompleteElsewhere()
        {
            await using var winner = fixture.Open();
            var session = await winner.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
            session.Status = terminal switch
            {
                "expired" => PlaidLinkSessionStatus.Expired,
                "exited" => PlaidLinkSessionStatus.Exited,
                "failed" => PlaidLinkSessionStatus.Failed,
                _ => PlaidLinkSessionStatus.Completed
            };
            session.BankConnectionId = fixture.BankId;
            session.ProtectedLinkToken = string.Empty;
            session.CompletedAtUtc = Fixture.Now.AddMinutes(2);
            session.UpdatedAtUtc = Fixture.Now.AddMinutes(2);
            if (terminal is "attention" or "disconnected")
            {
                var bank = await winner.BankConnections.SingleAsync(x => x.Id == fixture.BankId);
                bank.Status = terminal == "attention"
                    ? BankConnectionStatus.RequiresAttention : BankConnectionStatus.Disconnected;
            }
            await winner.SaveChangesAsync();
        }

        if (phase == "before-poll") await CompleteElsewhere();
        using var handler = new Provider(async () =>
        {
            if (phase == "during-poll") await CompleteElsewhere();
        });
        using var http = new HttpClient(handler);
        var service = fixture.Service(db, http, handler);
        var result = await service.CheckAndCompleteAsync(fixture.Owner, fixture.SessionId);
        var expected = terminal switch
        {
            "expired" => "Expired", "exited" => "Exited", "failed" => "Failed", _ => "Completed"
        };
        Assert.Equal(expected, result.Status);
        Assert.Equal(fixture.SessionId, result.SessionId);
        if (terminal is "active" or "attention")
        {
            Assert.NotNull(result.Connection);
            Assert.Equal(fixture.BankId, result.Connection.Id);
            Assert.Equal(terminal == "attention" ? "RequiresAttention" : "Active", result.Connection.Status);
        }
        else Assert.Null(result.Connection);
        Assert.Equal(phase == "during-poll" ? 1 : 0, handler.Polls);
        Assert.Equal(0, handler.Exchanges);

        // Returning durable evidence does not rewrite caller tracking or save it.
        Assert.Equal(PlaidLinkSessionStatus.Pending, tracked.Status);
        Assert.Equal(oldToken, tracked.ProtectedLinkToken);
        Assert.Equal(Fixture.Now.AddMinutes(1), tracked.UpdatedAtUtc);
        Assert.Equal(EntityState.Added, db.Entry(callerBill).State);
        await using (var before = fixture.Open())
            Assert.False(await before.BillStreams.AnyAsync(x => x.Id == callerBill.Id));
        await db.SaveChangesAsync();
        await using var verify = fixture.Open();
        var durable = await verify.PlaidLinkSessions.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.SessionId);
        Assert.Equal(expected, durable.Status.ToString());
        Assert.Equal(string.Empty, durable.ProtectedLinkToken);
        Assert.Equal(Fixture.Now.AddMinutes(2), durable.CompletedAtUtc);
        Assert.True(await verify.BillStreams.AnyAsync(x => x.Id == callerBill.Id));
        await fixture.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("update")]
    public async Task ForeignBankReference_IsRejectedByRelationalConstraint_OrHiddenByApplication(string mode)
    {
        var fixture = await Fixture.CreateAsync(mode);
        await using var db = fixture.Open();
        await db.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        await using var other = fixture.Open();
        var terminal = await other.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        terminal.Status = PlaidLinkSessionStatus.Completed;
        terminal.BankConnectionId = fixture.ForeignBankId;
        terminal.ProtectedLinkToken = string.Empty;
        if (other.Database.IsRelational())
        {
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, postgres.SqlState);
            await using var verify = fixture.Open();
            Assert.Equal(PlaidLinkSessionStatus.Pending,
                (await verify.PlaidLinkSessions.AsNoTracking().SingleAsync(x => x.Id == fixture.SessionId)).Status);
            await fixture.AssertHistoryAsync(verify);
        }
        else
        {
            // InMemory does not enforce relational constraints. Retain the
            // independent application defense without weakening PostgreSQL.
            await other.SaveChangesAsync();
            using var handler = new Provider();
            using var http = new HttpClient(handler);
            var result = await fixture.Service(db, http, handler)
                .CheckAndCompleteAsync(fixture.Owner, fixture.SessionId);
            Assert.Equal("Completed", result.Status);
            Assert.Null(result.Connection);
            Assert.Equal(0, handler.Calls);
            await fixture.AssertHistoryAsync(other);
        }
    }

    [Theory]
    [InlineData("initial", false)]
    [InlineData("initial", true)]
    [InlineData("existing", false)]
    [InlineData("existing", true)]
    [InlineData("update", false)]
    [InlineData("update", true)]
    public async Task CommittedSaveThenLostAcknowledgement_ReplaysDurableResult(
        string mode, bool callerEdit)
    {
        var loss = new LoseCompletionAcknowledgement();
        var fixture = await Fixture.CreateAsync(mode, loss);
        await using var db = fixture.Open();
        var session = await db.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        if (callerEdit) session.UpdatedAtUtc = Fixture.Now.AddMinutes(1);
        var originalToken = session.ProtectedLinkToken;
        using var handler = new Provider();
        using var http = new HttpClient(handler);
        var service = fixture.Service(db, http, handler);
        loss.Armed = true;

        await Assert.ThrowsAsync<IOException>(() =>
            service.CheckAndCompleteAsync(fixture.Owner, fixture.SessionId));
        Assert.Equal(1, loss.Losses);
        Assert.Equal(PlaidLinkSessionStatus.Pending, session.Status);
        Assert.Equal(originalToken, session.ProtectedLinkToken);
        await using (var committed = fixture.Open())
        {
            var persisted = await committed.PlaidLinkSessions.AsNoTracking()
                .SingleAsync(x => x.Id == fixture.SessionId);
            Assert.Equal(PlaidLinkSessionStatus.Completed, persisted.Status);
            Assert.NotNull(persisted.BankConnectionId);
            Assert.Equal(string.Empty, persisted.ProtectedLinkToken);
        }

        var calls = handler.Calls;
        var replay = await service.CheckAndCompleteAsync(fixture.Owner, fixture.SessionId);
        Assert.Equal("Completed", replay.Status);
        Assert.NotNull(replay.Connection);
        Assert.Equal(calls, handler.Calls);
        Assert.Equal(mode == "update" ? 0 : 1, handler.Exchanges);
        // A later caller save cannot turn the confirmed completion back to Pending.
        var bill = new BillStreamEntity { UserId = fixture.Owner, ProviderName = "After acknowledgement loss" };
        db.Add(bill);
        await db.SaveChangesAsync();
        await using var verify = fixture.Open();
        var stored = await verify.PlaidLinkSessions.AsNoTracking()
            .SingleAsync(x => x.Id == fixture.SessionId);
        Assert.Equal(PlaidLinkSessionStatus.Completed, stored.Status);
        Assert.Equal(replay.Connection.Id, stored.BankConnectionId);
        Assert.Equal(string.Empty, stored.ProtectedLinkToken);
        Assert.True(await verify.BillStreams.AnyAsync(x => x.Id == bill.Id));
        await fixture.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("bank")]
    [InlineData("token")]
    [InlineData("expiry")]
    [InlineData("deleted")]
    [InlineData("added")]
    public async Task UnsavedLifecycleChanges_FailClosedWithoutDiscardingCallerState(string change)
    {
        var fixture = await Fixture.CreateAsync("initial");
        await using var db = fixture.Open();
        var session = await db.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        switch (change)
        {
            case "status": session.Status = PlaidLinkSessionStatus.Completed; break;
            case "bank": session.BankConnectionId = fixture.BankId; break;
            case "token": session.ProtectedLinkToken = fixture.Protector.ProtectLinkToken("changed"); break;
            case "expiry": session.ExpiresAtUtc = session.ExpiresAtUtc.AddMinutes(1); break;
            case "deleted": db.Remove(session); break;
            case "added": db.Entry(session).State = EntityState.Added; break;
        }
        db.ChangeTracker.DetectChanges();
        var state = db.Entry(session).State;
        var values = (PlaidLinkSessionEntity)db.Entry(session).CurrentValues.ToObject();
        using var handler = new Provider();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service(db, http, handler).CheckAndCompleteAsync(fixture.Owner, fixture.SessionId));
        Assert.Equal(0, handler.Calls);
        Assert.Equal(state, db.Entry(session).State);
        Assert.Equal(values.Status, session.Status);
        Assert.Equal(values.BankConnectionId, session.BankConnectionId);
        Assert.Equal(values.ProtectedLinkToken, session.ProtectedLinkToken);
        Assert.Equal(values.ExpiresAtUtc, session.ExpiresAtUtc);
        Assert.Equal(values.UpdatedAtUtc, session.UpdatedAtUtc);
        await using var verify = fixture.Open();
        Assert.Equal(PlaidLinkSessionStatus.Pending,
            (await verify.PlaidLinkSessions.AsNoTracking().SingleAsync(x => x.Id == fixture.SessionId)).Status);
    }

    [Theory]
    [InlineData("before-poll", "token")]
    [InlineData("before-poll", "bank")]
    [InlineData("before-poll", "expiry")]
    [InlineData("before-poll", "deleted")]
    [InlineData("during-poll", "token")]
    [InlineData("during-poll", "bank")]
    [InlineData("during-poll", "expiry")]
    [InlineData("during-poll", "deleted")]
    public async Task ChangedDurablePendingState_IsNotUsedForAnOldProviderResponse(string phase, string change)
    {
        var fixture = await Fixture.CreateAsync("initial");
        await using var db = fixture.Open();
        var tracked = await db.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        var oldToken = tracked.ProtectedLinkToken;
        async Task ChangeElsewhere()
        {
            await using var other = fixture.Open();
            var session = await other.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
            switch (change)
            {
                case "token": session.ProtectedLinkToken = fixture.Protector.ProtectLinkToken("replacement"); break;
                case "bank": session.BankConnectionId = fixture.BankId; break;
                case "expiry": session.ExpiresAtUtc = session.ExpiresAtUtc.AddMinutes(1); break;
                case "deleted": other.Remove(session); break;
            }
            await other.SaveChangesAsync();
        }
        if (phase == "before-poll") await ChangeElsewhere();
        using var handler = new Provider(async () =>
        {
            if (phase == "during-poll") await ChangeElsewhere();
        });
        using var http = new HttpClient(handler);
        var error = await Record.ExceptionAsync(() =>
            fixture.Service(db, http, handler).CheckAndCompleteAsync(fixture.Owner, fixture.SessionId));
        if (change == "deleted") Assert.IsType<KeyNotFoundException>(error);
        else Assert.IsType<InvalidOperationException>(error);
        Assert.Equal(phase == "during-poll" ? 1 : 0, handler.Polls);
        Assert.Equal(0, handler.Exchanges);
        Assert.Equal(PlaidLinkSessionStatus.Pending, tracked.Status);
        Assert.Equal(oldToken, tracked.ProtectedLinkToken);
        Assert.Equal(EntityState.Unchanged, db.Entry(tracked).State);
        await using var verify = fixture.Open();
        await fixture.AssertHistoryAsync(verify);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignOrMissingSession_RemainsNotFoundEvenWhenTracked(bool foreign)
    {
        var fixture = await Fixture.CreateAsync("initial");
        await using var db = fixture.Open();
        await db.PlaidLinkSessions.SingleAsync(x => x.Id == fixture.SessionId);
        var owner = foreign ? fixture.ForeignOwner : fixture.Owner;
        var id = foreign ? fixture.SessionId : Guid.NewGuid();
        using var handler = new Provider();
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            fixture.Service(db, http, handler).CheckAndCompleteAsync(owner, id));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("initial")]
    [InlineData("existing")]
    [InlineData("update")]
    public async Task OrdinaryCompletionAndReplay_PreserveHistoryAndCallerWork(string mode)
    {
        var fixture = await Fixture.CreateAsync(mode);
        await using var db = fixture.Open();
        var bill = new BillStreamEntity { UserId = fixture.Owner, ProviderName = "Ordinary caller work" };
        db.Add(bill);
        using var handler = new Provider();
        using var http = new HttpClient(handler);
        var service = fixture.Service(db, http, handler);
        var result = await service.CheckAndCompleteAsync(fixture.Owner, fixture.SessionId);
        Assert.Equal("Completed", result.Status);
        Assert.NotNull(result.Connection);
        Assert.Equal(mode == "update" ? 0 : 1, handler.Exchanges);
        var calls = handler.Calls;
        var replay = await service.CheckAndCompleteAsync(fixture.Owner, fixture.SessionId);
        Assert.Equal(result, replay);
        Assert.Equal(calls, handler.Calls);
        await using var verify = fixture.Open();
        Assert.True(await verify.BillStreams.AnyAsync(x => x.Id == bill.Id));
        await fixture.AssertHistoryAsync(verify);
    }

    [Fact]
    public async Task CanceledReplay_DoesNotReadOrContactProvider()
    {
        var fixture = await Fixture.CreateAsync("initial");
        await using var db = fixture.Open();
        using var handler = new Provider();
        using var http = new HttpClient(handler);
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service(db, http, handler).CheckAndCompleteAsync(fixture.Owner, fixture.SessionId, stop.Token));
        Assert.Equal(0, handler.Calls);
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UnfinishedProviderPoll_RemainsPendingWithoutSavingCallerWork()
    {
        var fixture = await Fixture.CreateAsync("initial");
        await using var db = fixture.Open();
        var bill = new BillStreamEntity { UserId = fixture.Owner, ProviderName = "Unsaved caller" };
        db.Add(bill);
        using var handler = new Provider { PollJson = """{"link_sessions":[]}""" };
        using var http = new HttpClient(handler);
        var result = await fixture.Service(db, http, handler)
            .CheckAndCompleteAsync(fixture.Owner, fixture.SessionId);
        Assert.Equal("Pending", result.Status);
        Assert.Null(result.Connection);
        Assert.Equal(1, handler.Polls);
        Assert.Equal(0, handler.Exchanges);
        Assert.Equal(EntityState.Added, db.Entry(bill).State);
        await using var verify = fixture.Open();
        Assert.False(await verify.BillStreams.AnyAsync(x => x.Id == bill.Id));
        await fixture.AssertHistoryAsync(verify);
    }

    private sealed class LoseCompletionAcknowledgement : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Losses { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<PlaidLinkSessionEntity>()
                .Any(x => x.Entity.Status == PlaidLinkSessionStatus.Completed))
            {
                Armed = false;
                Losses++;
                throw new IOException("Synthetic acknowledgement loss after a successful database save.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Provider(Func<Task>? beforePoll = null) : HttpMessageHandler
    {
        public string ItemId { get; set; } = "unused";
        public string? PollJson { get; set; }
        public int Polls { get; private set; }
        public int Exchanges { get; private set; }
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            string json;
            switch (request.RequestUri!.AbsolutePath)
            {
                case "/link/token/get":
                    Polls++;
                    if (beforePoll is not null) await beforePoll();
                    json = PollJson ?? """{"link_sessions":[{"finished_at":"2026-10-24T00:00:00Z","results":{"item_add_results":[{"public_token":"synthetic-one-time-token"}]}}]}""";
                    break;
                case "/item/public_token/exchange":
                    Exchanges++;
                    if (Exchanges > 1) throw new InvalidOperationException("One-time token replayed.");
                    json = System.Text.Json.JsonSerializer.Serialize(new
                        { access_token = "synthetic-access-token", item_id = ItemId });
                    break;
                case "/item/get":
                    json = System.Text.Json.JsonSerializer.Serialize(new
                        { item = new { item_id = ItemId, institution_name = "Synthetic fixture bank" } });
                    break;
                default: throw new InvalidOperationException("Unexpected provider endpoint.");
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Fixture
    {
        public static readonly DateTimeOffset Now = new(2026, 10, 24, 0, 0, 0, TimeSpan.Zero);
        private DbContextOptions<FullWorthDbContext> Options { get; init; } = null!;
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid ForeignOwner { get; } = Guid.NewGuid();
        public Guid BankId { get; } = Guid.NewGuid();
        public Guid ForeignBankId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();
        private Guid AccountId { get; } = Guid.NewGuid();
        private Guid ForeignAccountId { get; } = Guid.NewGuid();
        private string ItemId => "fixture-item-" + BankId.ToString("N");
        public PlaidTokenProtector Protector { get; } = new(new EphemeralDataProtectionProvider());
        public FullWorthDbContext Open() => new(Options);

        public static async Task<Fixture> CreateAsync(string mode, SaveChangesInterceptor? interceptor = null)
        {
            var builder = new DbContextOptionsBuilder<FullWorthDbContext>();
            var optIn = Environment.GetEnvironmentVariable("FULLWORTH_HOSTED_REPLAY_POSTGRES");
            if (optIn is null) builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            else
            {
                if (optIn != "ci-fixture-only")
                    throw new InvalidOperationException("Explicit disposable-fixture opt-in is required.");
                var connection = new NpgsqlConnectionStringBuilder(
                    Environment.GetEnvironmentVariable("FULLWORTH_HOSTED_REPLAY_DATABASE")
                    ?? throw new InvalidOperationException("Disposable fixture connection is missing."));
                if (connection.Host != "127.0.0.1" || connection.Database != "fullworth_hosted_replay_fixture" ||
                    connection.Username != "fixture" || connection.Port != 5432)
                    throw new InvalidOperationException("Only the fixed loopback disposable fixture is allowed.");
                builder.UseNpgsql(connection.ConnectionString);
            }
            if (interceptor is not null) builder.AddInterceptors(interceptor);
            var fixture = new Fixture { Options = builder.Options };
            await using var db = fixture.Open();
            if (optIn is not null) await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = fixture.Owner },
                new ApplicationUser { Id = fixture.ForeignOwner });
            db.BankConnections.AddRange(new BankConnectionEntity
            {
                Id = fixture.BankId, UserId = fixture.Owner, InstitutionName = "Owned fixture",
                PlaidItemId = mode == "initial" ? "unrelated-" + fixture.ItemId : fixture.ItemId,
                ProtectedPlaidAccessToken = fixture.Protector.Protect("original-owned-credential"),
                TransactionsCursor = "retained-owned-cursor", LastSuccessfulSyncAtUtc = Now,
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            }, new BankConnectionEntity
            {
                Id = fixture.ForeignBankId, UserId = fixture.ForeignOwner, InstitutionName = "Foreign fixture",
                PlaidItemId = "foreign-" + fixture.ItemId,
                ProtectedPlaidAccessToken = fixture.Protector.Protect("retained-foreign-credential"),
                TransactionsCursor = "retained-foreign-cursor", LastSuccessfulSyncAtUtc = Now,
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            });
            db.BankAccounts.AddRange(new BankAccountEntity
            {
                Id = fixture.AccountId, UserId = fixture.Owner, BankConnectionId = fixture.BankId,
                PlaidAccountId = "owned-" + fixture.ItemId, Name = "Owned synthetic account"
            }, new BankAccountEntity
            {
                Id = fixture.ForeignAccountId, UserId = fixture.ForeignOwner, BankConnectionId = fixture.ForeignBankId,
                PlaidAccountId = "foreign-" + fixture.ItemId, Name = "Foreign synthetic account"
            });
            foreach (var (owner, account) in new[] { (fixture.Owner, fixture.AccountId),
                         (fixture.ForeignOwner, fixture.ForeignAccountId) })
                db.BankTransactions.Add(new BankTransactionEntity
                {
                    UserId = owner, BankAccountId = account, PlaidTransactionId = Guid.NewGuid().ToString("N"),
                    Name = "Synthetic history", Amount = 19.25m, IsoCurrencyCode = "EUR",
                    PostedDate = new DateOnly(2026, 10, 1), CreatedAtUtc = Now, UpdatedAtUtc = Now
                });
            db.PlaidLinkSessions.Add(new PlaidLinkSessionEntity
            {
                Id = fixture.SessionId, UserId = fixture.Owner,
                BankConnectionId = mode == "update" ? fixture.BankId : null,
                ProtectedLinkToken = fixture.Protector.ProtectLinkToken("synthetic-link-token"),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(2),
                CreatedAtUtc = Now, UpdatedAtUtc = Now
            });
            await db.SaveChangesAsync();
            return fixture;
        }

        public PlaidHostedLinkCompletionService Service(FullWorthDbContext db, HttpClient http, Provider handler)
        {
            handler.ItemId = ItemId;
            var api = new PlaidApiClient(http, Microsoft.Extensions.Options.Options.Create(new PlaidOptions
            {
                ClientId = "fixture-client", Secret = "synthetic-fixture-secret",
                Environment = PlaidOptions.SandboxEnvironment
            }));
            return new PlaidHostedLinkCompletionService(db, api, Protector,
                new PlaidConnectionExchangeService(api, Protector, db));
        }

        public async Task AssertHistoryAsync(FullWorthDbContext db)
        {
            var banks = await db.BankConnections.AsNoTracking()
                .Where(x => x.Id == BankId || x.Id == ForeignBankId).ToListAsync();
            Assert.Equal(2, banks.Count);
            Assert.Equal("retained-owned-cursor", banks.Single(x => x.Id == BankId).TransactionsCursor);
            var foreign = banks.Single(x => x.Id == ForeignBankId);
            Assert.Equal("retained-foreign-cursor", foreign.TransactionsCursor);
            Assert.Equal("retained-foreign-credential", Protector.Unprotect(foreign.ProtectedPlaidAccessToken!));
            Assert.Equal(Now, foreign.LastSuccessfulSyncAtUtc);
            var history = await db.BankTransactions.AsNoTracking()
                .Where(x => x.UserId == Owner || x.UserId == ForeignOwner).ToListAsync();
            Assert.Equal(2, history.Count);
            Assert.All(history, x =>
            {
                Assert.Equal(19.25m, x.Amount);
                Assert.Equal("EUR", x.IsoCurrencyCode);
                Assert.False(x.IsRemoved);
                Assert.Equal(Now, x.UpdatedAtUtc);
            });
        }
    }
}

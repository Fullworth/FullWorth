using System.Net;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FullWorth.Tests.RelationalBankLifecycle;

public static class BankLifecycleRaceChecks
{
    private static readonly DateTimeOffset OriginalTime = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    private static readonly DateTimeOffset WinnerTime = OriginalTime.AddHours(1);

    public static async Task<int> RunAsync()
    {
        // Deliberately refuse arbitrary hosts/databases. This executable is
        // only for the new workflow's disposable, loopback PostgreSQL service.
        var value = Environment.GetEnvironmentVariable("FULLWORTH_DISPOSABLE_POSTGRES");
        var connection = new NpgsqlConnectionStringBuilder(value ?? "");
        if (Environment.GetEnvironmentVariable("FULLWORTH_ALLOW_DISPOSABLE_POSTGRES") != "ci-fixture-only" ||
            connection.Host != "127.0.0.1" || connection.Database != "fullworth_bank_lifecycle_fixture" ||
            connection.Username != "fixture")
            throw new InvalidOperationException("Disposable PostgreSQL fixture configuration is required.");
        var options = new DbContextOptionsBuilder<FullWorthDbContext>().UseNpgsql(connection.ConnectionString).Options;
        await using (var db = new FullWorthDbContext(options))
            await db.Database.EnsureCreatedAsync();
        var count = 0;
        foreach (var transactions in new[] { false, true })
        {
            foreach (var duringSave in new[] { false, true })
            foreach (var replaceCredential in new[] { false, true })
            foreach (var attention in new[] { false, true })
            {
                Console.WriteLine($"Race: transactions={transactions}, beforeSave={duringSave}, replacement={replaceCredential}, attention={attention}");
                await RaceAsync(options, transactions, duringSave, replaceCredential, attention);
                count++;
            }
            foreach (var staleTracked in new[] { false, true })
            {
                await PreflightAsync(options, transactions, staleTracked, replaceCredential: false);
                count++;
            }
            await PreflightAsync(options, transactions, staleTracked: true, replaceCredential: true);
            count++;
            await ForeignOwnerAsync(options, transactions);
            count++;
            await SuccessAsync(options, transactions);
            count++;
        }
        Console.WriteLine($"PostgreSQL bank lifecycle checks passed: {count} cases; actual EF save concurrency and transaction rollback.");
        return 0;
    }

    public static async Task RaceAsync(DbContextOptions<FullWorthDbContext> plain, bool transactions,
        bool duringSave, bool replaceCredential, bool attention)
    {
        var fixture = await SeedAsync(plain, attention);
        var interceptor = new BeforeSave();
        var options = new DbContextOptionsBuilder<FullWorthDbContext>(plain).AddInterceptors(interceptor).Options;
        await using var scan = new FullWorthDbContext(options);
        var bank = await scan.BankConnections.SingleAsync(x => x.Id == fixture.Bank.Id);
        await scan.BankAccounts.Where(x => x.BankConnectionId == bank.Id).LoadAsync();
        await scan.BankTransactions.Where(x => x.UserId == bank.UserId).LoadAsync();
        var caller = new BillStreamEntity { UserId = bank.UserId, ProviderName = "Synthetic caller work" };
        scan.Add(caller);
        async Task Win() => await CommitWinnerAsync(plain, fixture, replaceCredential);
        using var handler = new Provider(fixture, duringSave ? null : Win);
        using var http = new HttpClient(handler);
        if (duringSave) interceptor.Action = Win;
        var error = await Capture(() => Sync(scan, fixture.Protector, http, bank.UserId, bank.Id, transactions));
        Require(error is DbUpdateConcurrencyException, "Stale sync must fail with a database concurrency conflict.");
        Require(handler.Calls == 1, "Each attempt may call the synthetic provider only once.");
        Require(scan.Entry(caller).State == EntityState.Added, "Caller insertion was discarded.");
        Require(scan.ChangeTracker.Entries<BankAccountEntity>().All(x => x.State != EntityState.Added),
            "Failed account insertion remains staged.");
        Require(scan.ChangeTracker.Entries<BankTransactionEntity>().All(x => x.State != EntityState.Added),
            "Failed transaction insertion remains staged.");
        await VerifyWinnerAndHistory(plain, fixture, replaceCredential);
        await scan.SaveChangesAsync();
        await VerifyWinnerAndHistory(plain, fixture, replaceCredential);
        await using (var verify = new FullWorthDbContext(plain))
            Require(await verify.BillStreams.AnyAsync(x => x.Id == caller.Id), "Caller work did not survive.");
        var retry = await Capture(() => Sync(scan, fixture.Protector, http, bank.UserId, bank.Id, transactions));
        Require(retry is InvalidOperationException && retry is not DbUpdateException,
            "A stale tracked retry must fail closed before provider access.");
        Require(handler.Calls == 1, "A stale retry sent an obsolete provider credential.");
    }

    public static async Task PreflightAsync(DbContextOptions<FullWorthDbContext> options,
        bool transactions, bool staleTracked, bool replaceCredential)
    {
        var fixture = await SeedAsync(options, attention: false);
        await using var scan = new FullWorthDbContext(options);
        if (staleTracked) await scan.BankConnections.SingleAsync(x => x.Id == fixture.Bank.Id);
        await CommitWinnerAsync(options, fixture, replaceCredential);
        using var handler = new Provider(fixture, null);
        using var http = new HttpClient(handler);
        var error = await Capture(() => Sync(scan, fixture.Protector, http, fixture.Bank.UserId,
            fixture.Bank.Id, transactions));
        Require(error is InvalidOperationException, "Unavailable connection did not fail closed.");
        Require(handler.Calls == 0, "Disconnected/stale credential reached the provider.");
        await VerifyWinnerAndHistory(options, fixture, replaceCredential);
    }

    public static async Task ForeignOwnerAsync(DbContextOptions<FullWorthDbContext> options, bool transactions)
    {
        var fixture = await SeedAsync(options, attention: false);
        await using var scan = new FullWorthDbContext(options);
        using var handler = new Provider(fixture, null);
        using var http = new HttpClient(handler);
        var error = await Capture(() => Sync(scan, fixture.Protector, http, fixture.Foreign.UserId,
            fixture.Bank.Id, transactions));
        Require(error is KeyNotFoundException, "Foreign ownership was not rejected as missing.");
        Require(handler.Calls == 0, "Foreign ownership reached the provider.");
    }

    public static async Task SuccessAsync(DbContextOptions<FullWorthDbContext> options, bool transactions)
    {
        var fixture = await SeedAsync(options, attention: true);
        await using var scan = new FullWorthDbContext(options);
        using var handler = new Provider(fixture, null);
        using var http = new HttpClient(handler);
        await Sync(scan, fixture.Protector, http, fixture.Bank.UserId, fixture.Bank.Id, transactions);
        await using var verify = new FullWorthDbContext(options);
        var bank = await verify.BankConnections.SingleAsync(x => x.Id == fixture.Bank.Id);
        Require(bank.Status == BankConnectionStatus.Active, "Normal synchronization failed.");
        Require(bank.ProtectedPlaidAccessToken == fixture.Bank.ProtectedPlaidAccessToken, "Credential changed.");
        if (transactions)
        {
            Require(bank.TransactionsCursor == "synthetic-final-cursor", "Normal cursor did not advance.");
            Require(await verify.BankTransactions.CountAsync(x => x.UserId == bank.UserId) == 3, "Normal delta missing.");
        }
        else
        {
            Require(bank.LastSuccessfulSyncAtUtc == OriginalTime, "Account synchronization changed transaction freshness.");
            Require(await verify.BankAccounts.CountAsync(x => x.BankConnectionId == bank.Id) == 2, "Normal account sync missing.");
        }
    }

    private static async Task Sync(FullWorthDbContext db, PlaidTokenProtector protector,
        HttpClient http, Guid owner, Guid bank, bool transactions)
    {
        var client = new PlaidApiClient(http, Options.Create(new PlaidOptions
            { ClientId = "synthetic-client", Secret = "synthetic-secret", Environment = "sandbox" }));
        if (transactions)
            await new PlaidTransactionSyncService(db, client, protector).SyncConnectionAsync(owner, bank);
        else
            await new PlaidAccountSyncService(db, client, protector).SyncAccountsAsync(owner, bank);
    }

    private static async Task CommitWinnerAsync(DbContextOptions<FullWorthDbContext> options,
        Fixture fixture, bool replaceCredential)
    {
        await using var db = new FullWorthDbContext(options);
        var bank = await db.BankConnections.SingleAsync(x => x.Id == fixture.Bank.Id);
        bank.Status = replaceCredential ? BankConnectionStatus.Active : BankConnectionStatus.Disconnected;
        bank.ProtectedPlaidAccessToken = replaceCredential ? fixture.Replacement : null;
        bank.TransactionsCursor = replaceCredential ? "synthetic-replacement-cursor" : null;
        bank.LastSuccessfulSyncAtUtc = WinnerTime;
        bank.UpdatedAtUtc = WinnerTime;
        var account = await db.BankAccounts.SingleAsync(x => x.Id == fixture.Account.Id);
        account.IsActive = replaceCredential;
        account.Name = "Synthetic winning account";
        account.UpdatedAtUtc = WinnerTime;
        await db.SaveChangesAsync();
    }

    private static async Task VerifyWinnerAndHistory(DbContextOptions<FullWorthDbContext> options,
        Fixture fixture, bool replaceCredential)
    {
        await using var db = new FullWorthDbContext(options);
        var bank = await db.BankConnections.AsNoTracking().SingleAsync(x => x.Id == fixture.Bank.Id);
        Require(bank.Status == (replaceCredential ? BankConnectionStatus.Active : BankConnectionStatus.Disconnected),
            "Stale sync overwrote winning lifecycle state.");
        Require(bank.ProtectedPlaidAccessToken == (replaceCredential ? fixture.Replacement : null),
            "Stale sync overwrote winning credential.");
        Require(bank.TransactionsCursor == (replaceCredential ? "synthetic-replacement-cursor" : null),
            "Stale sync overwrote winning cursor.");
        Require(bank.LastSuccessfulSyncAtUtc == WinnerTime && bank.UpdatedAtUtc == WinnerTime,
            "Stale sync overwrote winning freshness.");
        var accounts = await db.BankAccounts.AsNoTracking().Where(x => x.BankConnectionId == bank.Id).ToArrayAsync();
        Require(accounts.Length == 1 && accounts[0].Name == "Synthetic winning account" &&
            accounts[0].IsActive == replaceCredential, "Failed account batch was partially committed.");
        var rows = await db.BankTransactions.AsNoTracking().Where(x => x.UserId == bank.UserId)
            .OrderBy(x => x.Amount).ToArrayAsync();
        Require(rows.Length == 2 && rows[0].Amount == 5m && rows[1].Amount == 10m &&
            rows.All(x => !x.IsRemoved), "Failed transaction batch was partially committed.");
        var foreign = await db.BankConnections.AsNoTracking().SingleAsync(x => x.Id == fixture.Foreign.Id);
        Require(foreign.Status == BankConnectionStatus.Active &&
            foreign.ProtectedPlaidAccessToken == fixture.Foreign.ProtectedPlaidAccessToken &&
            foreign.TransactionsCursor == "synthetic-original-cursor", "Foreign owner was mutated.");
        var foreignAccount = await db.BankAccounts.AsNoTracking().SingleAsync(x => x.BankConnectionId == foreign.Id);
        Require(foreignAccount.IsActive && foreignAccount.Name == "Synthetic foreign account",
            "Foreign account was mutated.");
        var foreignTransaction = await db.BankTransactions.AsNoTracking().SingleAsync(x => x.UserId == foreign.UserId);
        Require(foreignTransaction.Amount == 99.50m && foreignTransaction.IsoCurrencyCode == "EUR" &&
            !foreignTransaction.IsRemoved && foreignTransaction.Name == "Synthetic foreign charge",
            "Foreign transaction history was mutated.");
    }

    private static async Task<Fixture> SeedAsync(DbContextOptions<FullWorthDbContext> options, bool attention)
    {
        var protector = new PlaidTokenProtector(new EphemeralDataProtectionProvider());
        await using var db = new FullWorthDbContext(options);
        var owner = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N") };
        var other = new ApplicationUser { Id = Guid.NewGuid(), UserName = Guid.NewGuid().ToString("N") };
        BankConnectionEntity Bank(Guid user, string token) => new()
        {
            UserId = user, InstitutionName = "Synthetic bank", PlaidItemId = Guid.NewGuid().ToString("N"),
            ProtectedPlaidAccessToken = protector.Protect(token), Status = BankConnectionStatus.Active,
            TransactionsCursor = "synthetic-original-cursor", LastSuccessfulSyncAtUtc = OriginalTime, UpdatedAtUtc = OriginalTime
        };
        var bank = Bank(owner.Id, "synthetic-original-access");
        if (attention) bank.Status = BankConnectionStatus.RequiresAttention;
        var foreign = Bank(other.Id, "synthetic-foreign-access");
        var account = new BankAccountEntity { UserId = owner.Id, BankConnectionId = bank.Id,
            PlaidAccountId = Guid.NewGuid().ToString("N"), Name = "Synthetic original account", IsActive = true };
        var foreignAccount = new BankAccountEntity { UserId = other.Id, BankConnectionId = foreign.Id,
            PlaidAccountId = Guid.NewGuid().ToString("N"), Name = "Synthetic foreign account", IsActive = true };
        db.AddRange(owner, other, bank, foreign, account, foreignAccount);
        db.Add(new BankTransactionEntity { UserId = other.Id, BankAccountId = foreignAccount.Id,
            PlaidTransactionId = "synthetic-foreign-transaction", Name = "Synthetic foreign charge",
            Amount = 99.50m, PostedDate = new DateOnly(2026, 1, 5), IsoCurrencyCode = "EUR" });
        db.AddRange(new BankTransactionEntity { UserId = owner.Id, BankAccountId = account.Id,
            PlaidTransactionId = "synthetic-modified", Name = "Synthetic original charge", Amount = 10m,
            PostedDate = new DateOnly(2026, 1, 5), IsoCurrencyCode = "USD" },
            new BankTransactionEntity { UserId = owner.Id, BankAccountId = account.Id,
                PlaidTransactionId = "synthetic-removed", Name = "Synthetic removed charge", Amount = 5m,
                PostedDate = new DateOnly(2026, 1, 10), IsoCurrencyCode = "USD" });
        await db.SaveChangesAsync();
        return new(bank, foreign, account, protector, protector.Protect("synthetic-replacement-access"));
    }

    private sealed record Fixture(BankConnectionEntity Bank, BankConnectionEntity Foreign,
        BankAccountEntity Account, PlaidTokenProtector Protector, string Replacement);

    private sealed class BeforeSave : SaveChangesInterceptor
    {
        public Func<Task>? Action { get; set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Action is { } action) { Action = null; await action(); }
            return result;
        }
    }

    private sealed class Provider(Fixture fixture, Func<Task>? beforeResponse) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            if (beforeResponse is not null) await beforeResponse();
            var account = fixture.Account.PlaidAccountId;
            object Transaction(string id, decimal amount) => new
            {
                transaction_id = id, account_id = account, name = "Synthetic provider charge",
                amount, date = "2026-02-05", pending = false, iso_currency_code = "USD"
            };
            object response = request.RequestUri!.AbsolutePath.EndsWith("/accounts/get", StringComparison.Ordinal)
                ? new { accounts = new[] { new { account_id = account, name = "Synthetic provider account",
                    type = "depository", subtype = "checking" },
                    new { account_id = "synthetic-added-account", name = "Synthetic added account",
                    type = "depository", subtype = "savings" } } }
                : new { added = new[] { Transaction("synthetic-added", 15m) },
                    modified = new[] { Transaction("synthetic-modified", 25m) },
                    removed = new[] { new { transaction_id = "synthetic-removed" } },
                    next_cursor = "synthetic-final-cursor", has_more = false };
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response),
                Encoding.UTF8, "application/json") };
        }
    }

    private static async Task<Exception?> Capture(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception error) { return error; }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

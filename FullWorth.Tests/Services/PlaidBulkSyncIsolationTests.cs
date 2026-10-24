using System.Net;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidBulkSyncIsolationTests
{
    private static readonly Guid FailedId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid HealthyId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset OldSync = DateTimeOffset.Parse("2026-08-01T00:00:00Z");

    [Theory]
    [InlineData(false, "attention")]
    [InlineData(true, "attention")]
    [InlineData(false, "transient")]
    [InlineData(true, "transient")]
    [InlineData(false, "timeout")]
    [InlineData(true, "timeout")]
    [InlineData(false, "save")]
    [InlineData(true, "save")]
    [InlineData(false, "attention-save")]
    [InlineData(true, "attention-save")]
    [InlineData(false, "invalid")]
    [InlineData(true, "invalid")]
    public async Task PartialFailure_RefreshesHealthyBankInSeparateContext_AndRethrows(bool transactions, string mode)
    {
        using var handler = new BankHandler(mode);
        using var http = new HttpClient(handler);
        var failure = new SaveFailure(mode);
        await using var services = Services(http, failure);
        await using var caller = services.CreateAsyncScope();
        var db = caller.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var user = await Seed(db, services.GetRequiredService<PlaidTokenProtector>());
        var tracked = await db.BankConnections.SingleAsync(c => c.Id == FailedId);
        var callerAccount = await db.BankAccounts.SingleAsync(a => a.BankConnectionId == FailedId);
        tracked.InstitutionName = "Unsaved caller bank edit";
        callerAccount.Name = "Unsaved caller account edit";
        failure.Contexts.Clear();
        failure.Armed = true;

        var error = await Record.ExceptionAsync(() =>
            Sync(caller.ServiceProvider.GetRequiredService<IBankDataSyncGateway>(), transactions, user));
        Assert.NotNull(error);
        if (mode is "save" or "attention-save") Assert.IsType<DbUpdateException>(error);
        else if (mode == "timeout") Assert.IsAssignableFrom<OperationCanceledException>(error);
        else if (mode == "invalid") Assert.IsType<InvalidOperationException>(error);
        else Assert.IsType<PlaidApiException>(error);
        Assert.Equal(new[] { "bank-a", "bank-b" }, handler.Calls.Select(c => c.Token).ToArray());
        Assert.All(handler.Calls, c => Assert.Equal(transactions ? "/transactions/sync" : "/accounts/get", c.Path));
        Assert.DoesNotContain(db.ContextId.InstanceId, failure.Contexts);
        if (mode is "save" or "attention-save")
            Assert.Equal(2, failure.Contexts.Distinct().Count());

        await using var verification = services.CreateAsyncScope();
        var stored = verification.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var failed = await stored.BankConnections.SingleAsync(c => c.Id == FailedId);
        var healthy = await stored.BankConnections.SingleAsync(c => c.Id == HealthyId);
        Assert.Equal(mode == "attention" ? BankConnectionStatus.RequiresAttention : BankConnectionStatus.Active, failed.Status);
        Assert.Equal("Original bank a", failed.InstitutionName);
        Assert.Equal("cursor-a-original", failed.TransactionsCursor);
        Assert.Equal(OldSync, failed.LastSuccessfulSyncAtUtc);
        Assert.Equal("Original account a",
            (await stored.BankAccounts.SingleAsync(a => a.BankConnectionId == FailedId)).Name);
        Assert.False(await stored.BankAccounts.AnyAsync(a => a.PlaidAccountId == "account-a-new"));
        Assert.False(await stored.BankTransactions.AnyAsync(t => t.PlaidTransactionId == "transaction-a"));
        if (transactions)
        {
            Assert.Equal("cursor-b-final", healthy.TransactionsCursor);
            Assert.True(healthy.LastSuccessfulSyncAtUtc > OldSync);
            Assert.True(await stored.BankTransactions.AnyAsync(t => t.PlaidTransactionId == "transaction-b"));
            Assert.Equal("Original account b",
                (await stored.BankAccounts.SingleAsync(a => a.BankConnectionId == HealthyId)).Name);
        }
        else
        {
            Assert.Equal("Provider account b",
                (await stored.BankAccounts.SingleAsync(a => a.PlaidAccountId == "account-b")).Name);
            Assert.True(await stored.BankAccounts.AnyAsync(a => a.PlaidAccountId == "account-b-new"));
            Assert.Equal(OldSync, healthy.LastSuccessfulSyncAtUtc);
            Assert.False(await stored.BankTransactions.AnyAsync());
        }

        Assert.Equal("Unsaved caller bank edit", tracked.InstitutionName);
        Assert.Equal("Unsaved caller account edit", callerAccount.Name);
        Assert.Empty(db.ChangeTracker.Entries<BankTransactionEntity>());
        Assert.DoesNotContain(handler.Calls, c => c.Token is "foreign" or "disconnected" or "attention");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulBulk_ReturnsCompleteCounts_AndIncludesArchivedActiveBank(bool transactions)
    {
        using var handler = new BankHandler();
        using var http = new HttpClient(handler);
        var failure = new SaveFailure("");
        await using var services = Services(http, failure);
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var user = await Seed(db, services.GetRequiredService<PlaidTokenProtector>());
        var gateway = scope.ServiceProvider.GetRequiredService<PlaidBankDataSyncGateway>();
        if (transactions)
        {
            var summary = await gateway.SyncAllTransactionsAsync(user);
            Assert.Equal(new PlaidTransactionSyncSummary(2, 2, 0, 0), summary);
        }
        else
        {
            var summary = await gateway.SyncAllAccountsAsync(user);
            Assert.Equal(new PlaidAccountSyncSummary(2, 4), summary);
        }
        Assert.Equal(new[] { "bank-a", "bank-b" }, handler.Calls.Select(c => c.Token).ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOwner_ReturnsZeroCounts_AndDoesNotCallProvider(bool transactions)
    {
        using var handler = new BankHandler();
        using var http = new HttpClient(handler);
        await using var services = Services(http, new SaveFailure(""));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        await Seed(db, services.GetRequiredService<PlaidTokenProtector>());
        var gateway = scope.ServiceProvider.GetRequiredService<PlaidBankDataSyncGateway>();
        if (transactions)
            Assert.Equal(new PlaidTransactionSyncSummary(0, 0, 0, 0),
                await gateway.SyncAllTransactionsAsync(Guid.NewGuid()));
        else
            Assert.Equal(new PlaidAccountSyncSummary(0, 0),
                await gateway.SyncAllAccountsAsync(Guid.NewGuid()));
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_StopsBeforeNextBank(bool transactions)
    {
        using var cancel = new CancellationTokenSource();
        using var handler = new BankHandler("caller-cancel") { Cancel = cancel.Cancel };
        using var http = new HttpClient(handler);
        await using var services = Services(http, new SaveFailure(""));
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var user = await Seed(db, services.GetRequiredService<PlaidTokenProtector>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Sync(scope.ServiceProvider.GetRequiredService<IBankDataSyncGateway>(), transactions, user, cancel.Token));
        Assert.Single(handler.Calls);
        Assert.False(await db.BankTransactions.AnyAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MultipleFailures_PreserveFirstProviderError(bool transactions)
    {
        using var handler = new BankHandler("both-fail");
        using var http = new HttpClient(handler);
        await using var services = Services(http, new SaveFailure(""));
        await using var scope = services.CreateAsyncScope();
        var user = await Seed(scope.ServiceProvider.GetRequiredService<FullWorthDbContext>(),
            services.GetRequiredService<PlaidTokenProtector>());
        var error = await Assert.ThrowsAsync<PlaidApiException>(() =>
            Sync(scope.ServiceProvider.GetRequiredService<IBankDataSyncGateway>(), transactions, user));
        Assert.Equal("ITEM_LOGIN_REQUIRED", error.ErrorCode);
        Assert.Equal(2, handler.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticatedBulkHttpRoute_AttemptsHealthyBank_AndDoesNotReportPartialSuccess(bool transactions)
    {
        using var handler = new BankHandler("attention");
        using var http = new HttpClient(handler);
        await using var factory = new FullWorthApiFactory();
        await using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<PlaidApiClient>();
            services.AddScoped(_ => Client(http));
        }));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false
        });
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        Guid user;
        await using (var seedScope = configured.Services.CreateAsyncScope())
        {
            var manager = seedScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            user = (await manager.FindByEmailAsync(session.Email))!.Id;
            await Seed(seedScope.ServiceProvider.GetRequiredService<FullWorthDbContext>(),
                seedScope.ServiceProvider.GetRequiredService<PlaidTokenProtector>(), user);
        }
        TestUserAuthentication.Authorize(client, session);
        using var response = await client.PostAsync(
            transactions ? "/api/plaid/transactions/sync" : "/api/plaid/accounts/sync", null);
        Assert.False(response.IsSuccessStatusCode);
        var message = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("bank-a", message);
        Assert.DoesNotContain("bank-b", message);
        Assert.Equal(new[] { "bank-a", "bank-b" }, handler.Calls.Select(c => c.Token).ToArray());
        await using var verify = configured.Services.CreateAsyncScope();
        var stored = verify.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        Assert.Equal(BankConnectionStatus.RequiresAttention,
            (await stored.BankConnections.SingleAsync(c => c.Id == FailedId)).Status);
        if (transactions)
            Assert.Equal("cursor-b-final", (await stored.BankConnections.SingleAsync(c => c.Id == HealthyId)).TransactionsCursor);
        else
            Assert.True(await stored.BankAccounts.AnyAsync(a => a.PlaidAccountId == "account-b-new"));
    }

    private static Task Sync(IBankDataSyncGateway gateway, bool transactions, Guid user,
        CancellationToken token = default) => transactions
            ? gateway.SyncTransactionsAsync(user, token) : gateway.SyncAccountsAsync(user, token);

    private static ServiceProvider Services(HttpClient http, SaveFailure failure)
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).AddInterceptors(failure).Options;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new FullWorthDbContext(options));
        services.AddScoped(_ => Client(http));
        services.AddSingleton(new PlaidTokenProtector(new EphemeralDataProtectionProvider()));
        services.AddSingleton(TestSecurityAlertAggregator.Create());
        services.AddScoped<PlaidAccountSyncService>();
        services.AddScoped<PlaidTransactionSyncService>();
        services.AddScoped<PlaidConnectionSyncCoordinator>();
        services.AddScoped<PlaidBankDataSyncGateway>();
        services.AddScoped<IBankDataSyncGateway, PlaidBankDataSyncGateway>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static PlaidApiClient Client(HttpClient http) => new(http, Options.Create(new PlaidOptions
    {
        ClientId = "fixture-client", Secret = "fixture-secret", Environment = PlaidOptions.SandboxEnvironment
    }));

    private static async Task<Guid> Seed(FullWorthDbContext db, PlaidTokenProtector protector, Guid? owner = null)
    {
        var user = owner ?? Guid.NewGuid();
        BankConnectionEntity Connection(Guid id, Guid userId, string token, BankConnectionStatus status = BankConnectionStatus.Active) =>
            new() { Id = id, UserId = userId, InstitutionName = "Original bank " + (token == "bank-a" ? "a" : "b"),
                PlaidItemId = "item-" + token, ProtectedPlaidAccessToken = protector.Protect(token),
                TransactionsCursor = token == "bank-a" ? "cursor-a-original" : "cursor-b-original",
                Status = status, LastSuccessfulSyncAtUtc = OldSync };
        var first = Connection(FailedId, user, "bank-a");
        var healthy = Connection(HealthyId, user, "bank-b");
        healthy.IsArchived = true; // Presentation archive must not disable background maintenance.
        var missing = Connection(Guid.NewGuid(), user, "missing"); missing.ProtectedPlaidAccessToken = null;
        var empty = Connection(Guid.NewGuid(), user, "empty"); empty.ProtectedPlaidAccessToken = "";
        db.BankConnections.AddRange(first, healthy, missing, empty,
            Connection(Guid.NewGuid(), Guid.NewGuid(), "foreign"),
            Connection(Guid.NewGuid(), user, "disconnected", BankConnectionStatus.Disconnected),
            Connection(Guid.NewGuid(), user, "attention", BankConnectionStatus.RequiresAttention));
        foreach (var (connection, suffix) in new[] { (first, "a"), (healthy, "b") })
            db.BankAccounts.Add(new BankAccountEntity { UserId = user, BankConnectionId = connection.Id,
                PlaidAccountId = "account-" + suffix, Name = "Original account " + suffix, IsActive = true });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return user;
    }

    private sealed class SaveFailure(string mode) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public List<Guid> Contexts { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!;
            Contexts.Add(db.ContextId.InstanceId);
            var stagedBankA = db.ChangeTracker.Entries<BankAccountEntity>()
                .Any(e => e.State == EntityState.Added && e.Entity.PlaidAccountId == "account-a-new") ||
                db.ChangeTracker.Entries<BankTransactionEntity>()
                .Any(e => e.State == EntityState.Added && e.Entity.PlaidTransactionId == "transaction-a");
            var stagedAttention = db.ChangeTracker.Entries<BankConnectionEntity>()
                .Any(e => e.Entity.Id == FailedId && e.Entity.Status == BankConnectionStatus.RequiresAttention);
            if (Armed && ((mode == "save" && stagedBankA) || (mode == "attention-save" && stagedAttention)))
            {
                Armed = false;
                throw new DbUpdateException("Synthetic pre-commit bulk failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class BankHandler(string mode = "") : HttpMessageHandler
    {
        public Action? Cancel { get; init; }
        public List<(string Path, string Token)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var input = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var bank = input.RootElement.GetProperty("access_token").GetString()!;
            var path = request.RequestUri!.AbsolutePath;
            Calls.Add((path, bank));
            if (bank is not ("bank-a" or "bank-b")) throw new InvalidOperationException("Unexpected owner or inactive bank.");
            if (bank == "bank-a")
            {
                if (mode == "caller-cancel") { Cancel!(); token.ThrowIfCancellationRequested(); }
                if (mode == "timeout") throw new TaskCanceledException("Synthetic upstream timeout.");
                if (mode is "attention" or "attention-save" or "both-fail")
                    return Json(HttpStatusCode.BadRequest, new { error_type = "ITEM_ERROR", error_code = "ITEM_LOGIN_REQUIRED" });
                if (mode == "transient")
                    return Json(HttpStatusCode.ServiceUnavailable, new { error_type = "API_ERROR", error_code = "INTERNAL_SERVER_ERROR" });
                if (mode == "invalid") return Json(HttpStatusCode.OK, new { accounts = "invalid" });
            }
            if (bank == "bank-b" && mode == "both-fail")
                return Json(HttpStatusCode.ServiceUnavailable, new { error_type = "API_ERROR", error_code = "INTERNAL_SERVER_ERROR" });
            var suffix = bank == "bank-a" ? "a" : "b";
            if (path == "/accounts/get") return Json(HttpStatusCode.OK, new { accounts = new[]
            {
                new { account_id = "account-" + suffix, name = "Provider account " + suffix, type = "depository", subtype = "checking" },
                new { account_id = "account-" + suffix + "-new", name = "New account " + suffix, type = "depository", subtype = "savings" }
            } });
            if (path == "/transactions/sync") return Json(HttpStatusCode.OK, new
            {
                added = new[] { new { transaction_id = "transaction-" + suffix, account_id = "account-" + suffix,
                    name = "Fixture service", amount = 10m, iso_currency_code = "USD", date = "2026-08-01", pending = false } },
                modified = Array.Empty<object>(), removed = Array.Empty<object>(), next_cursor = "cursor-" + suffix + "-final", has_more = false
            });
            throw new InvalidOperationException("Unexpected sync endpoint.");
        }
        private static HttpResponseMessage Json(HttpStatusCode code, object value) =>
            new(code) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}

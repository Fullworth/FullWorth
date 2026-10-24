using System.Reflection;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Plaid;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class BankMonitoringStableSweepTests
{
    private static readonly Guid First = Id(10);
    private static readonly Guid Second = Id(20);
    private static readonly Guid Third = Id(30);
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");

    [Theory]
    [InlineData("attention")]
    [InlineData("discovery")]
    [InlineData("transient")]
    [InlineData("concurrent-removal")]
    public async Task ChangingDueSet_DoesNotSkipLaterUsersOrRetryBeforeSweepEnd(string mode)
    {
        var probes = new Probes { Mode = mode };
        await using var services = Services(probes);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            db.BankConnections.AddRange(Connection(First), Connection(Second), Connection(Third));
            // Multiple banks must not consume multiple positions in a user sweep.
            db.BankConnections.Add(Connection(Second));
            await db.SaveChangesAsync();
        }

        using var worker = Worker(services);
        Assert.Equal(1, await Pass(worker, 1));
        if (mode == "concurrent-removal")
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            (await db.BankConnections.SingleAsync(c => c.UserId == First)).Status = BankConnectionStatus.Disconnected;
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await Pass(worker, 1));
        Assert.Equal(1, await Pass(worker, 1));
        Assert.Equal(new[] { First, Second, Third }, probes.Attempts);
        Assert.Equal(0, await Pass(worker, 1));
        Assert.Equal(3, probes.Attempts.Count);
        Assert.Equal(mode == "transient" ? 1 : 0, await Pass(worker, 1));
        Assert.Equal(mode == "transient" ? new[] { First, Second, Third, First } : new[] { First, Second, Third },
            probes.Attempts);
    }

    [Fact]
    public async Task NewLowerIdentity_WaitsForNextSweep_WithoutDisplacingUnprocessedUsers()
    {
        var probes = new Probes();
        await using var services = Services(probes);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            db.BankConnections.AddRange(Connection(First), Connection(Third));
            await db.SaveChangesAsync();
        }
        using var worker = Worker(services);
        Assert.Equal(1, await Pass(worker, 1));
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            db.BankConnections.AddRange(Connection(Id(5)), Connection(Second));
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await Pass(worker, 1));
        Assert.Equal(1, await Pass(worker, 1));
        Assert.Equal(0, await Pass(worker, 1));
        Assert.Equal(new[] { First, Second, Third }, probes.Attempts);
        Assert.Equal(1, await Pass(worker, 1));
        Assert.Equal(Id(5), probes.Attempts[^1]);
    }

    [Fact]
    public async Task CallerCancellation_StopsBeforeNextUser()
    {
        using var cancellation = new CancellationTokenSource();
        var probes = new Probes { Cancel = cancellation.Cancel };
        await using var services = Services(probes);
        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            db.BankConnections.AddRange(Connection(First), Connection(Second));
            await db.SaveChangesAsync();
        }
        using var worker = Worker(services);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Pass(worker, 2, cancellation.Token));
        Assert.Equal(new[] { First }, probes.Attempts);
    }

    [Fact]
    public async Task KeysetQuery_IsBoundedDistinctAndFiltersIneligibleConnections()
    {
        await using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var now = DateTimeOffset.UtcNow;
        db.BankConnections.AddRange(Connection(First), Connection(Second), Connection(Second), Connection(Third));
        var attention = Connection(Id(40)); attention.Status = BankConnectionStatus.RequiresAttention;
        var disconnected = Connection(Id(50)); disconnected.Status = BankConnectionStatus.Disconnected;
        var empty = Connection(Id(60)); empty.ProtectedPlaidAccessToken = "";
        var fresh = Connection(Id(70)); fresh.LastSuccessfulSyncAtUtc = now;
        var missing = Connection(Id(80)); missing.ProtectedPlaidAccessToken = null;
        db.BankConnections.AddRange(attention, disconnected, empty, fresh, missing);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var gateway = new PlaidBankConnectionReadGateway(db);
        Assert.Equal(new[] { Second }, await gateway.GetDueUserIdsAfterAsync(now, TimeSpan.FromHours(6), 1, First));
        Assert.Equal(new[] { Third }, await gateway.GetDueUserIdsAfterAsync(now, TimeSpan.FromHours(6), 10, Second));
        Assert.Equal(new[] { First, Second, Third }, await gateway.GetDueUserIdsAfterAsync(now, TimeSpan.FromHours(6), 10, null));
        Assert.Empty(db.ChangeTracker.Entries());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gateway.GetDueUserIdsAfterAsync(now, TimeSpan.Zero, 1, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => gateway.GetDueUserIdsAfterAsync(now, TimeSpan.FromHours(6), 0, null));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            gateway.GetDueUserIdsAfterAsync(now, TimeSpan.FromHours(6), 1, null, cancellation.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionProvider_TranslatesActualKeysetQueryWithoutOffset(bool continuation)
    {
        // SQL translation only: no PostgreSQL server, credentials, or recovery claim.
        using var db = new FullWorthDbContext(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only").Options);
        var sql = new PlaidBankConnectionReadGateway(db)
            .QueryDueUserIdsAfter(DateTimeOffset.UtcNow, TimeSpan.FromHours(6), 25, continuation ? First : null)
            .ToQueryString();
        Assert.Contains("SELECT DISTINCT", sql);
        Assert.Contains("ORDER BY", sql);
        Assert.Contains("LIMIT", sql);
        Assert.DoesNotContain("OFFSET", sql);
        if (continuation) Assert.Contains(">", sql);
    }

    private static BankConnectionEntity Connection(Guid userId) => new()
    {
        UserId = userId, InstitutionName = "Synthetic bank", ProtectedPlaidAccessToken = "synthetic-token",
        LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow.AddDays(-2)
    };

    private static ServiceProvider Services(Probes probes)
    {
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => new FullWorthDbContext(options));
        services.AddSingleton(probes);
        services.AddScoped<IBankDataSyncGateway, Sync>();
        services.AddScoped<IBankConnectionReadGateway, PlaidBankConnectionReadGateway>();
        services.AddScoped<BillMonitoringRefreshScheduler>();
        services.AddScoped<BankConnectionHealthAlertService>();
        services.AddScoped<RecurringBillDiscoveryPersistenceService>();
        services.AddScoped<BillMonitoringRefreshService>();
        services.AddSingleton<IBankTransactionDiscoveryGateway>(probes);
        services.AddSingleton<IBillTransactionAssociationGateway>(probes);
        services.AddSingleton<IPlanningPaydayAlertRefreshGateway>(probes);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static BillMonitoringBackgroundService Worker(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new BillMonitoringBackgroundOptions()),
        NullLogger<BillMonitoringBackgroundService>.Instance);
    private static Task<int> Pass(BillMonitoringBackgroundService worker, int batchSize, CancellationToken token = default) =>
        (Task<int>)typeof(BillMonitoringBackgroundService).GetMethod("RunPassAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(worker, [TimeSpan.FromHours(6), batchSize, token])!;

    private sealed class Sync(FullWorthDbContext db, Probes probes) : IBankDataSyncGateway
    {
        public async Task SyncAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            probes.Attempts.Add(userId);
            if (probes.Cancel is not null)
            {
                probes.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            if (userId == First && probes.Mode is "transient" or "concurrent-removal")
                throw new InvalidOperationException("Synthetic transient failure.");
            foreach (var connection in await db.BankConnections.Where(c => c.UserId == userId).ToListAsync(cancellationToken))
            {
                if (userId == First && probes.Mode == "attention")
                    connection.Status = BankConnectionStatus.RequiresAttention;
                else connection.LastSuccessfulSyncAtUtc = DateTimeOffset.UtcNow;
            }
            await db.SaveChangesAsync(cancellationToken);
            if (userId == First && probes.Mode == "attention")
                throw new InvalidOperationException("Synthetic attention failure after status save.");
        }
        public Task SyncAccountsAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SyncTransactionsAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Probes : IBankTransactionDiscoveryGateway, IBillTransactionAssociationGateway, IPlanningPaydayAlertRefreshGateway
    {
        public string Mode { get; init; } = "";
        public Action? Cancel { get; init; }
        public List<Guid> Attempts { get; } = [];
        public Task<IReadOnlyList<BankTransactionDiscoveryRecord>> GetDiscoveryTransactionsAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            if (userId == First && Mode == "discovery")
                throw new InvalidOperationException("Synthetic discovery failure after successful bank sync.");
            return Task.FromResult<IReadOnlyList<BankTransactionDiscoveryRecord>>([]);
        }
        public Task<IReadOnlyList<BillTransactionAssociationRecord>> GetAsync(Guid userId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BillTransactionAssociationRecord>>([]);
        public Task StageAssignmentsAsync(Guid userId, IReadOnlyCollection<BillTransactionAssociationAssignment> assignments,
            DateTimeOffset updatedAtUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<PlanningPaydayAlertRefreshResult> RefreshAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PlanningPaydayAlertRefreshResult(0, 0, 0, false));
    }
}

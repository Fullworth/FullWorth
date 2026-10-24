using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Planning;
using FullWorth.Core.Models.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace FullWorth.Tests.Services;

public sealed partial class MonitoringReliabilityBatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private const string HealthTitle = "Bank connection needs attention";
    private const string HealthMessage = "Fixture Bank needs attention before FullWorth can continue reliable bank monitoring. Review or reconnect it in Connect.";
    private sealed class Clock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Fixture
    {
        public DbContextOptions<FullWorthDbContext> Options { get; private set; } = null!;
        public Guid Owner { get; } = Guid.NewGuid();
        public Guid Foreign { get; } = Guid.NewGuid();
        public Guid Bill { get; } = Guid.NewGuid();
        public Guid ForeignBill { get; } = Guid.NewGuid();
        public Guid Bank { get; } = Guid.NewGuid();
        public Guid ForeignBank { get; } = Guid.NewGuid();

        public FullWorthDbContext Open(SaveChangesInterceptor? interceptor = null) =>
            interceptor is null ? new(Options) :
                new(new DbContextOptionsBuilder<FullWorthDbContext>(Options).AddInterceptors(interceptor).Options);

        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            var builder = new DbContextOptionsBuilder<FullWorthDbContext>();
            var flag = Environment.GetEnvironmentVariable("FULLWORTH_MONITORING_BATCH_POSTGRES");
            if (flag is null) builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            else
            {
                if (flag != "ci-fixture-only")
                    throw new InvalidOperationException("Explicit disposable fixture opt-in is required.");
                var connection = new NpgsqlConnectionStringBuilder(
                    Environment.GetEnvironmentVariable("FULLWORTH_MONITORING_BATCH_DATABASE")
                    ?? throw new InvalidOperationException("Fixture connection is missing."));
                if (connection.Host != "127.0.0.1" || connection.Port != 5432 ||
                    connection.Database != "fullworth_monitoring_batch_fixture" || connection.Username != "fixture")
                    throw new InvalidOperationException("Only the fixed disposable loopback database is allowed.");
                builder.UseNpgsql(connection.ConnectionString);
            }
            f.Options = builder.Options;
            await using var db = f.Open();
            if (flag is not null) await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = f.Owner }, new ApplicationUser { Id = f.Foreign });
            db.BillStreams.AddRange(new BillStreamEntity { Id = f.Bill, UserId = f.Owner,
                ProviderName = "Owned fixture", Source = BillStreamSource.Manual },
                new BillStreamEntity { Id = f.ForeignBill, UserId = f.Foreign,
                    ProviderName = "Foreign fixture", Source = BillStreamSource.Manual });
            foreach (var (owner, bank) in new[] { (f.Owner, f.Bank), (f.Foreign, f.ForeignBank) })
            {
                db.BankConnections.Add(new BankConnectionEntity { Id = bank, UserId = owner,
                    PlaidItemId = "fixture-" + bank.ToString("N"), InstitutionName = "Fixture Bank",
                    ProtectedPlaidAccessToken = "synthetic-protected-marker", TransactionsCursor = "retained-cursor",
                    LastSuccessfulSyncAtUtc = Now, UpdatedAtUtc = Now, CreatedAtUtc = Now });
                var account = new BankAccountEntity { UserId = owner, BankConnectionId = bank,
                    PlaidAccountId = "account-" + bank.ToString("N"), Name = "Synthetic account" };
                db.Add(account);
                db.BankTransactions.Add(new BankTransactionEntity { UserId = owner, BankAccountId = account.Id,
                    PlaidTransactionId = Guid.NewGuid().ToString("N"), Name = "Synthetic retained history",
                    Amount = 17.25m, IsoCurrencyCode = "EUR", PostedDate = new(2026, 10, 1),
                    CreatedAtUtc = Now, UpdatedAtUtc = Now });
            }
            db.PlanningPaySchedules.Add(new PlanningPayScheduleEntity { UserId = f.Owner,
                Frequency = PayScheduleFrequency.Biweekly, AnchorPayDate = new(2026, 10, 24),
                DefaultPaychecksAhead = 2, CreatedAtUtc = Now, UpdatedAtUtc = Now });
            await db.SaveChangesAsync();
            return f;
        }

        public async Task AssertHistoryAsync(FullWorthDbContext db)
        {
            var banks = await db.BankConnections.AsNoTracking()
                .Where(x => x.Id == Bank || x.Id == ForeignBank).ToListAsync();
            Assert.Equal(2, banks.Count);
            Assert.All(banks, x =>
            {
                Assert.Equal("synthetic-protected-marker", x.ProtectedPlaidAccessToken);
                Assert.Equal("retained-cursor", x.TransactionsCursor);
                Assert.Equal(Now, x.LastSuccessfulSyncAtUtc);
            });
            var history = await db.BankTransactions.AsNoTracking()
                .Where(x => x.UserId == Owner || x.UserId == Foreign).ToListAsync();
            Assert.Equal(2, history.Count);
            Assert.All(history, x =>
            {
                Assert.Equal(17.25m, x.Amount);
                Assert.Equal("EUR", x.IsoCurrencyCode);
                Assert.False(x.IsRemoved);
                Assert.Equal(Now, x.UpdatedAtUtc);
            });
        }
    }

    private sealed class WriteFault : SaveChangesInterceptor
    {
        public Func<FullWorthDbContext, bool> Matches { get; set; } = _ => false;
        public Exception Error { get; set; } = new DbUpdateException("Synthetic write failure.");
        public Func<Task>? BeforeFailure { get; set; }
        public Action? Cancel { get; set; }
        public bool Armed { get; set; }
        public bool UseRealUniqueConstraint { get; set; }
        public int Failures { get; private set; }
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var db = (FullWorthDbContext)eventData.Context!;
            if (!Armed || !Matches(db)) return result;
            Armed = false;
            Failures++;
            if (BeforeFailure is not null) await BeforeFailure();
            Cancel?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            if (UseRealUniqueConstraint && db.Database.IsRelational()) return result;
            throw Error;
        }
    }

    private static Exception Error(string kind) => kind switch
    {
        "database" => new DbUpdateException("Synthetic database failure."),
        "provider-cancel" => new OperationCanceledException("Independent operation canceled."),
        _ => new IOException("Synthetic operation failure.")
    };

    private static BillAlertEntity Health(Guid owner, string? message = null) => new()
    {
        UserId = owner, AlertType = BillAlertType.ConnectionIssue, Title = HealthTitle,
        Severity = BillAlertSeverity.Warning, Message = message ?? HealthMessage,
        CreatedAtUtc = Now, UpdatedAtUtc = Now, IsRead = true, IsDismissed = true
    };

    private static PlanningPaydayAlertRequest Request(Guid source) =>
        new(source, PlanningPaydayAlertSeverity.Info, "Fixture payday", "Immutable synthetic guidance");

    private static BillAlertEntity Payday(Guid owner, PlanningPaydayAlertRequest request) => new()
    {
        UserId = owner, SourceEventId = request.SourceEventId,
        AlertType = BillAlertType.PaydayPlan, Severity = request.Severity switch { PlanningPaydayAlertSeverity.Info => BillAlertSeverity.Info,
            PlanningPaydayAlertSeverity.Warning => BillAlertSeverity.Warning, _ => BillAlertSeverity.Critical },
        Title = request.Title, Message = request.Message, CreatedAtUtc = Now, UpdatedAtUtc = Now
    };

    private sealed class Banks(Guid owner, IReadOnlyList<string> names) : IBankConnectionReadGateway
    {
        public Task<IReadOnlyList<string>> GetAttentionInstitutionNamesAsync(Guid userId,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(owner, userId);
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(names);
        }
        public Task<IReadOnlyList<Guid>> GetDueUserIdsAfterAsync(DateTimeOffset now, TimeSpan cadence,
            int maxUsers, Guid? afterUserId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetDueUserIdsAsync(DateTimeOffset now, TimeSpan cadence,
            int maxUsers, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record EntryState(EntityState State, Dictionary<string, object?> Current,
        Dictionary<string, object?> Original, string[] Modified);
    private static Dictionary<object, EntryState> Capture(FullWorthDbContext db)
    {
        db.ChangeTracker.DetectChanges();
        return db.ChangeTracker.Entries().ToDictionary(e => e.Entity, e =>
            new EntryState(e.State, e.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue),
                e.Properties.ToDictionary(p => p.Metadata.Name, p => p.OriginalValue),
                e.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name).Order().ToArray()));
    }
    private static void AssertRetained(FullWorthDbContext db, Dictionary<object, EntryState> before)
    {
        db.ChangeTracker.DetectChanges();
        foreach (var (entity, saved) in before)
        {
            var e = db.Entry(entity);
            Assert.Equal(saved.State, e.State);
            foreach (var (name, value) in saved.Current) Assert.Equal(value, e.Property(name).CurrentValue);
            foreach (var (name, value) in saved.Original) Assert.Equal(value, e.Property(name).OriginalValue);
            Assert.Equal(saved.Modified, e.Properties.Where(p => p.IsModified)
                .Select(p => p.Metadata.Name).Order().ToArray());
        }
    }

    private sealed class EmptyBills : IBillStreamReadGateway
    {
        public Task<IReadOnlyList<BillStreamReadRecord>> ListOwnedActiveAsync(Guid userId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<BillStreamReadRecord>>([]);
        public Task<BillStreamReadRecord?> GetOwnedAsync(Guid userId, Guid id,
            CancellationToken cancellationToken = default) => Task.FromResult<BillStreamReadRecord?>(null);
    }

    private sealed class UnusedFacts : IBillPlanningFactsGateway
    {
        public Task<IReadOnlyDictionary<Guid, BillPlanningFact>> GetLatestAsync(Guid userId,
            IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Empty bill sets must not request statement facts.");
    }

    private sealed class Payrolls(Guid owner) : IPlanningPostedPayrollFactsGateway
    {
        public List<PlanningPostedPayrollFact> Facts { get; } = Enumerable.Range(0, 3)
            .Select(i => new PlanningPostedPayrollFact(Guid.NewGuid(), 1000m,
                new DateOnly(2026, 10, 22).AddDays(i), "USD")).ToList();
        public List<Guid> Attempts { get; } = [];
        public Action<PlanningPostedPayrollFact>? BeforeIndividual { get; set; }
        public int Reads { get; private set; }
        public Task<IReadOnlyList<PlanningPostedPayrollFact>> GetAsync(Guid userId,
            DateOnly fromInclusive, DateOnly throughInclusive, CancellationToken cancellationToken = default)
        {
            Assert.Equal(owner, userId);
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            var matches = Facts.Where(x => x.PostedDate >= fromInclusive &&
                x.PostedDate <= throughInclusive).ToArray();
            if (fromInclusive == throughInclusive)
            {
                foreach (var fact in matches)
                {
                    Attempts.Add(fact.TransactionId);
                    BeforeIndividual?.Invoke(fact);
                }
            }
            return Task.FromResult<IReadOnlyList<PlanningPostedPayrollFact>>(matches);
        }
    }

    private static PlanningPaydayAlertRefreshService Refresh(Fixture f, FullWorthDbContext db,
        Payrolls payrolls, IPlanningPaydayAlertGateway? alerts = null)
    {
        var streams = new EmptyBills();
        var settings = new PlanningSettingsService(db, streams, new Clock());
        var store = new PlanningPaycheckAllocationStore(db, new Clock());
        var plan = new PlanningPaydayPlanService(settings, store, streams, new UnusedFacts(), payrolls);
        return new PlanningPaydayAlertRefreshService(plan, payrolls,
            alerts ?? new PlanningPaydayAlertGateway(db), new Clock());
    }
}

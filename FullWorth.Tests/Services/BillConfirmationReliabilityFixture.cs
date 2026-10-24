using System.Security.Claims;
using FullWorth.API.Controllers;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FullWorth.Tests.Services;

public sealed partial class BillConfirmationReliabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
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
            var flag = Environment.GetEnvironmentVariable("FULLWORTH_CONFIRMATION_POSTGRES");
            if (flag is null) builder.UseInMemoryDatabase(Guid.NewGuid().ToString("N"));
            else
            {
                if (flag != "ci-fixture-only")
                    throw new InvalidOperationException("Explicit disposable fixture opt-in is required.");
                var connection = new NpgsqlConnectionStringBuilder(
                    Environment.GetEnvironmentVariable("FULLWORTH_CONFIRMATION_DATABASE")
                    ?? throw new InvalidOperationException("Fixture connection is missing."));
                if (connection.Host != "127.0.0.1" || connection.Port != 5432 ||
                    connection.Database != "fullworth_confirmation_fixture" || connection.Username != "fixture")
                    throw new InvalidOperationException("Only the fixed disposable loopback database is allowed.");
                builder.UseNpgsql(connection.ConnectionString);
            }
            f.Options = builder.Options;
            await using var db = f.Open();
            if (flag is not null) await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id = f.Owner }, new ApplicationUser { Id = f.Foreign });
            db.BillStreams.AddRange(new BillStreamEntity { Id = f.Bill, UserId = f.Owner,
                ProviderName = "Target", Source = BillStreamSource.AutomaticDiscovery, IsActive = true },
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
        public bool LetSaveProceed { get; set; }
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
            if (LetSaveProceed && db.Database.IsRelational()) return result;
            throw Error;
        }
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

    private sealed class Host : IDisposable
    {
        private readonly ServiceProvider _services;
        public BillStreamsController Controller { get; }
        public Host(FullWorthDbContext db, Guid? owner)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton(db);
            services.AddIdentityCore<ApplicationUser>().AddEntityFrameworkStores<FullWorthDbContext>();
            _services = services.BuildServiceProvider();
            Controller = new BillStreamsController(db,
                _services.GetRequiredService<UserManager<ApplicationUser>>(), null!, null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(owner.HasValue
                            ? [new Claim(ClaimTypes.NameIdentifier, owner.Value.ToString())]
                            : Array.Empty<Claim>(), "fixture"))
                    }
                }
            };
        }
        public void Dispose() => _services.Dispose();
    }

    private static async Task<IActionResult> Act(Host host, string operation, Guid id,
        CancellationToken token = default)
    {
        if (operation == "confirm") return await host.Controller.ConfirmBillStream(id, token);
        return (await host.Controller.CreateBillStream(new("Target", "Internet"), token)).Result!;
    }

    private static BillStreamResult Created(IActionResult result) =>
        Assert.IsType<BillStreamResult>(Assert.IsType<OkObjectResult>(result).Value);
}

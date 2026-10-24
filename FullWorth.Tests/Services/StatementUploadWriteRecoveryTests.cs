using System.Security.Claims;
using System.Text;
using FullWorth.API.Controllers;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Statements;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class StatementUploadWriteRecoveryTests
{
    public static IEnumerable<object[]> Failures()
    {
        foreach (var mode in new[] { "database", "cancel", "unexpected" })
        foreach (var callerChanges in new[] { false, true })
            yield return [mode, callerChanges];
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task FailedMetadataInsert_RemovesFileAndCandidate_PreservesCaller_AndRetries(
        string mode, bool callerChanges)
    {
        await using var factory = new FullWorthApiFactory();
        using var identityScope = factory.Services.CreateScope();
        var name = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var failure = new UploadFailure(mode);
        var options = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(name, root).AddInterceptors(failure).Options;
        var plain = new DbContextOptionsBuilder<FullWorthDbContext>().UseInMemoryDatabase(name, root).Options;
        var storageRoot = Path.Combine(Path.GetTempPath(), "FullWorth-upload-recovery", Guid.NewGuid().ToString());
        var storage = new SecureBillStatementStorageService(Options.Create(new BillStatementStorageOptions
            { RootPath = storageRoot, MaxFileSizeBytes = 1024 * 1024 }),
            factory.Services.GetRequiredService<IWebHostEnvironment>());
        try
        {
            await using var db = new FullWorthDbContext(options);
            var owner = Guid.NewGuid();
            var stream = new BillStreamEntity { UserId = owner, ProviderName = "Fixture" };
            var modified = new BankConnectionEntity { UserId = owner, InstitutionName = "Original" };
            var deleted = new BankConnectionEntity { UserId = owner, InstitutionName = "Deleted caller" };
            var added = new BankConnectionEntity { UserId = owner, InstitutionName = "Added caller" };
            db.AddRange(stream, modified, deleted);
            await db.SaveChangesAsync();
            if (callerChanges)
            {
                modified.InstitutionName = "Unsaved caller";
                db.Remove(deleted);
                db.Add(added);
            }
            db.ChangeTracker.DetectChanges();
            var states = db.ChangeTracker.Entries<BankConnectionEntity>().ToDictionary(x => x.Entity.Id, x => x.State);
            var flags = db.Entry(modified).Properties.Where(x => x.IsModified).Select(x => x.Metadata.Name).Order().ToArray();
            var controller = new BillStatementUploadsController(db,
                identityScope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), storage,
                new BillStatementProcessingSignal(), new BillStreamReadGateway(db))
            {
                ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, owner.ToString())], "fixture"))
                }}
            };
            using var stop = new CancellationTokenSource();
            failure.Cancel = stop.Cancel;
            failure.Armed = true;
            using var bytes = new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.4\nfixture document\n%%EOF"));
            var file = new FormFile(bytes, 0, bytes.Length, "file", "fixture.pdf");
            var error = await Record.ExceptionAsync(() => controller.UploadStatement(stream.Id, file, stop.Token));
            if (mode == "cancel") Assert.IsAssignableFrom<OperationCanceledException>(error);
            else if (mode == "database") Assert.IsType<DbUpdateException>(error);
            else Assert.IsType<InvalidOperationException>(error);
            Assert.NotNull(failure.Candidate);
            Assert.Equal(EntityState.Detached, db.Entry(failure.Candidate).State);
            Assert.Empty(Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories));
            foreach (var (id, state) in states)
                Assert.Equal(state, db.ChangeTracker.Entries<BankConnectionEntity>().Single(x => x.Entity.Id == id).State);
            Assert.Equal(flags, db.Entry(modified).Properties.Where(x => x.IsModified)
                .Select(x => x.Metadata.Name).Order().ToArray());
            Assert.Equal("Original", db.Entry(modified).OriginalValues.GetValue<string>(nameof(modified.InstitutionName)));

            // A later save must not write metadata referencing the removed file.
            await db.SaveChangesAsync();
            await using (var verify = new FullWorthDbContext(plain))
            {
                Assert.Empty(await verify.BillStatementUploads.ToListAsync());
                Assert.Equal(callerChanges ? "Unsaved caller" : "Original",
                    (await verify.BankConnections.SingleAsync(x => x.Id == modified.Id)).InstitutionName);
                Assert.Equal(!callerChanges, await verify.BankConnections.AnyAsync(x => x.Id == deleted.Id));
                Assert.Equal(callerChanges, await verify.BankConnections.AnyAsync(x => x.Id == added.Id));
            }
            bytes.Position = 0;
            var result = await controller.UploadStatement(stream.Id, file);
            Assert.IsType<CreatedResult>(result.Result);
            var saved = await db.BillStatementUploads.SingleAsync();
            using var stored = storage.OpenRead(owner, saved.StorageKey);
            Assert.True(stored.Length > 8);
            Assert.Equal(BillStatementUploadStatus.Uploaded, saved.Status);
        }
        finally { Directory.Delete(storageRoot, recursive: true); }
    }

    private sealed class UploadFailure(string mode) : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public Action? Cancel { get; set; }
        public BillStatementUploadEntity? Candidate { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Armed = false;
            Candidate = eventData.Context!.ChangeTracker.Entries<BillStatementUploadEntity>()
                .Single(x => x.State == EntityState.Added).Entity;
            if (mode == "cancel") { Cancel!(); cancellationToken.ThrowIfCancellationRequested(); }
            if (mode == "unexpected") throw new InvalidOperationException("Synthetic upload failure.");
            throw new DbUpdateException("Synthetic pre-commit metadata failure.");
        }
    }
}

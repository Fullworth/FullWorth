using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Services.Statements;
using FullWorth.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FullWorth.Tests.Security;

public sealed class AccountDeletionQuarantineRecoveryTests
{
    [Fact]
    public async Task Reconcile_RestoresQuarantinedStatementWhenUserStillExists()
    {
        using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var userId = await TestUserAuthentication.GetUserIdAsync(
            factory,
            session.Email);

        await using var scope = factory.Services.CreateAsyncScope();

        var storage =
            scope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();

        var logger =
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<AccountDeletionStatementQuarantineRecovery>();

        var stored = await StoreTestPdfAsync(
            storage,
            userId);

        var entry = storage.QuarantineForAccountDeletion(
            userId,
            stored.StorageKey);

        Assert.True(entry.WasPresent);
        Assert.Throws<FileNotFoundException>(
            () => storage.OpenRead(userId, stored.StorageKey));

        var recovery = new AccountDeletionStatementQuarantineRecovery(
            dbContext,
            storage,
            logger);

        var reconciled = await recovery.ReconcileAsync();

        Assert.Equal(1, reconciled);
        Assert.Empty(storage.GetPendingAccountDeletionQuarantineEntries());

        using var restored = storage.OpenRead(
            userId,
            stored.StorageKey);

        Assert.True(restored.Length > 0);
    }

    [Fact]
    public async Task Reconcile_PurgesQuarantinedStatementWhenUserNoLongerExists()
    {
        using var factory = new FullWorthApiFactory();
        _ = factory.CreateHttpsClient();

        var deletedUserId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();

        var storage =
            scope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

        var dbContext =
            scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();

        var logger =
            scope.ServiceProvider
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger<AccountDeletionStatementQuarantineRecovery>();

        var stored = await StoreTestPdfAsync(
            storage,
            deletedUserId);

        var entry = storage.QuarantineForAccountDeletion(
            deletedUserId,
            stored.StorageKey);

        Assert.True(entry.WasPresent);

        var recovery = new AccountDeletionStatementQuarantineRecovery(
            dbContext,
            storage,
            logger);

        var reconciled = await recovery.ReconcileAsync();

        Assert.Equal(1, reconciled);
        Assert.Empty(storage.GetPendingAccountDeletionQuarantineEntries());

        Assert.Throws<FileNotFoundException>(
            () => storage.OpenRead(
                deletedUserId,
                stored.StorageKey));
    }

    [Fact]
    public async Task Reconcile_MultipleEntriesAcrossUsers_RestoresAndPurgesCorrectly()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        var existingUserId =
            await TestUserAuthentication
                .GetUserIdAsync(
                    factory,
                    session.Email);

        var deletedUserId =
            Guid.NewGuid();

        await using var scope =
            factory.Services
                .CreateAsyncScope();

        var storage =
            scope.ServiceProvider
                .GetRequiredService<
                    SecureBillStatementStorageService>();

        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<
                    FullWorthDbContext>();

        var logger =
            scope.ServiceProvider
                .GetRequiredService<
                    ILoggerFactory>()
                .CreateLogger<
                    AccountDeletionStatementQuarantineRecovery>();

        var existingFiles =
            new[]
            {
                await StoreTestPdfAsync(
                    storage,
                    existingUserId),

                await StoreTestPdfAsync(
                    storage,
                    existingUserId)
            };

        var deletedFiles =
            new[]
            {
                await StoreTestPdfAsync(
                    storage,
                    deletedUserId),

                await StoreTestPdfAsync(
                    storage,
                    deletedUserId)
            };

        foreach (var file in
                 existingFiles)
        {
            storage.QuarantineForAccountDeletion(
                existingUserId,
                file.StorageKey);
        }

        foreach (var file in
                 deletedFiles)
        {
            storage.QuarantineForAccountDeletion(
                deletedUserId,
                file.StorageKey);
        }

        var recovery =
            new AccountDeletionStatementQuarantineRecovery(
                dbContext,
                storage,
                logger);

        var reconciled =
            await recovery.ReconcileAsync();

        Assert.Equal(
            4,
            reconciled);

        Assert.Empty(
            storage.GetPendingAccountDeletionQuarantineEntries());

        foreach (var file in
                 existingFiles)
        {
            using var restored =
                storage.OpenRead(
                    existingUserId,
                    file.StorageKey);

            Assert.True(
                restored.Length >
                0);
        }

        foreach (var file in
                 deletedFiles)
        {
            Assert.Throws<
                FileNotFoundException>(
                () =>
                    storage.OpenRead(
                        deletedUserId,
                        file.StorageKey));
        }
    }

    [Fact]
    public async Task StorageQuarantine_CanBeRestoredOrCommittedWithoutPathEscape()
    {
        using var factory = new FullWorthApiFactory();
        _ = factory.CreateHttpsClient();

        var userId = Guid.NewGuid();

        await using var scope = factory.Services.CreateAsyncScope();

        var storage =
            scope.ServiceProvider.GetRequiredService<SecureBillStatementStorageService>();

        var restoredFile = await StoreTestPdfAsync(
            storage,
            userId);

        var restoredEntry = storage.QuarantineForAccountDeletion(
            userId,
            restoredFile.StorageKey);

        storage.RestoreAccountDeletionQuarantine(restoredEntry);

        using (var restored = storage.OpenRead(
                   userId,
                   restoredFile.StorageKey))
        {
            Assert.True(restored.Length > 0);
        }

        var committedFile = await StoreTestPdfAsync(
            storage,
            userId);

        var committedEntry = storage.QuarantineForAccountDeletion(
            userId,
            committedFile.StorageKey);

        storage.CommitAccountDeletionQuarantine(committedEntry);

        Assert.Throws<FileNotFoundException>(
            () => storage.OpenRead(
                userId,
                committedFile.StorageKey));

        Assert.Throws<InvalidOperationException>(
            () => storage.QuarantineForAccountDeletion(
                Guid.NewGuid(),
                restoredFile.StorageKey));
    }

    private static async Task<StoredBillStatementFile> StoreTestPdfAsync(
        SecureBillStatementStorageService storage,
        Guid userId)
    {
        await using var stream = new MemoryStream(
            Encoding.ASCII.GetBytes(
                "%PDF-1.4\n%%EOF"));

        return await storage.StoreAsync(
            userId,
            stream,
            "statement.pdf");
    }
}

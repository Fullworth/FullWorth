using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.RelationalBankLifecycle;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class BankSyncLifecycleConcurrencyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisconnectedConnection_NeverSendsCredential_EvenWhenTrackedStateIsStale(
        bool transactions, bool staleTracked) =>
        await BankLifecycleRaceChecks.PreflightAsync(Options(), transactions, staleTracked, replaceCredential: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacedCredential_NeverUsesTrackedOldCredential(bool transactions) =>
        await BankLifecycleRaceChecks.PreflightAsync(Options(), transactions, staleTracked: true, replaceCredential: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForeignConnection_IsMissingBeforeProviderAccess(bool transactions) =>
        await BankLifecycleRaceChecks.ForeignOwnerAsync(Options(), transactions);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryRequiresAttentionRetry_StillSucceeds(bool transactions) =>
        await BankLifecycleRaceChecks.SuccessAsync(Options(), transactions);

    [Theory]
    [InlineData(nameof(BankConnectionEntity.Status))]
    [InlineData(nameof(BankConnectionEntity.ProtectedPlaidAccessToken))]
    public void LifecycleAndCredential_AreConcurrencyPredicates(string property)
    {
        using var db = new FullWorthDbContext(Options());
        Assert.True(db.Model.FindEntityType(typeof(BankConnectionEntity))!.FindProperty(property)!.IsConcurrencyToken);
    }

    private static DbContextOptions<FullWorthDbContext> Options() =>
        new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
}

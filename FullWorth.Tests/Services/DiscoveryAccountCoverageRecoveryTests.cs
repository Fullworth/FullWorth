using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Bills;
using FullWorth.API.Services.Contracts;
using FullWorth.API.Services.Plaid;
using FullWorth.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FullWorth.Tests.Services;

public sealed class DiscoveryAccountCoverageRecoveryTests
{
    private static readonly DateOnly Start = new(2026, 1, 1);

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public async Task OtherAccountHistory_CannotSupplySourceCoverage(int sourceDays, bool canDeactivate)
    {
        await using var fixture = new Fixture();
        var sourceAccount = Guid.NewGuid();
        fixture.LinkPayment(sourceAccount);
        fixture.AddHistory(sourceAccount, sourceDays);
        fixture.AddHistory(Guid.NewGuid(), 120);
        await fixture.VerifyAsync(canDeactivate);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public async Task EveryLinkedAccount_MustMeetMinimumCoverage(int secondAccountDays, bool canDeactivate)
    {
        await using var fixture = new Fixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        fixture.LinkPayment(first);
        fixture.LinkPayment(second, Start.AddDays(1));
        fixture.AddHistory(first, 60);
        fixture.AddHistory(second, secondAccountDays);
        await fixture.VerifyAsync(canDeactivate);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("removed")]
    [InlineData("credit")]
    [InlineData("missing-merchant")]
    public async Task IneligibleRows_CannotExtendSourceCoverage(string excludedKind)
    {
        await using var fixture = new Fixture();
        var sourceAccount = Guid.NewGuid();
        fixture.LinkPayment(sourceAccount);
        var excluded = fixture.Charge(sourceAccount, Start.AddDays(90), "Unrelated coverage");
        switch (excludedKind)
        {
            case "pending": excluded.IsPending = true; break;
            case "removed": excluded.IsRemoved = true; break;
            case "credit": excluded.Amount = -20m; break;
            case "missing-merchant": excluded.Name = " "; excluded.MerchantName = null; break;
        }
        fixture.AddHistory(Guid.NewGuid(), 120);
        await fixture.VerifyAsync(canDeactivate: false);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingAccountMetadata_RetainsBillAndLinks(bool emptyGuid)
    {
        await using var fixture = new Fixture();
        var sourceAccount = Guid.NewGuid();
        fixture.LinkPayment(sourceAccount);
        fixture.AddHistory(sourceAccount, 90);
        var gateway = new MissingAccountGateway(
            new PlaidBankTransactionDiscoveryGateway(fixture.Db), emptyGuid);
        await fixture.VerifyAsync(canDeactivate: false, gateway);
    }

    [Fact]
    public async Task AnotherOwnersHistory_CannotSupplyCoverageEvenForMatchingOpaqueAccountId()
    {
        await using var fixture = new Fixture();
        var sourceAccount = Guid.NewGuid();
        fixture.LinkPayment(sourceAccount);
        fixture.Charge(sourceAccount, Start.AddDays(90), "Foreign coverage", Guid.NewGuid());
        await fixture.VerifyAsync(canDeactivate: false);
    }

    [Fact]
    public async Task MissingOrRemovedLinkedEvidence_DoesNotAuthorizeDeactivation()
    {
        await using var fixture = new Fixture();
        var sourceAccount = Guid.NewGuid();
        fixture.LinkPayment(sourceAccount).IsRemoved = true;
        fixture.AddHistory(sourceAccount, 90);
        await fixture.VerifyAsync(canDeactivate: false);
    }

    [Fact]
    public async Task BillWithoutSourceLinks_IsRetainedInsteadOfBorrowingGlobalCoverage()
    {
        await using var fixture = new Fixture();
        fixture.AddHistory(Guid.NewGuid(), 90);
        await fixture.VerifyAsync(canDeactivate: false);
    }

    [Fact]
    public async Task ManualBill_RemainsActiveEvenWithCompleteSourceCoverage()
    {
        await using var fixture = new Fixture();
        fixture.Bill.Source = BillStreamSource.Manual;
        var account = Guid.NewGuid();
        fixture.LinkPayment(account);
        fixture.AddHistory(account, 90);
        await fixture.VerifyAsync(canDeactivate: false);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private static readonly DateTimeOffset OriginalTime = new(2025, 12, 1, 0, 0, 0, TimeSpan.Zero);
        public FullWorthDbContext Db { get; } = new(new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase($"discovery-account-coverage-{Guid.NewGuid():N}").Options);
        public Guid Owner { get; } = Guid.NewGuid();
        public BillStreamEntity Bill { get; }

        public Fixture()
        {
            Bill = new BillStreamEntity
            {
                UserId = Owner, ProviderName = "Synthetic Internet", Category = BillCategory.Internet,
                Source = BillStreamSource.AutomaticDiscovery, IsActive = true,
                CreatedAtUtc = OriginalTime, UpdatedAtUtc = OriginalTime
            };
            Db.BillStreams.Add(Bill);
        }

        public BankTransactionEntity LinkPayment(Guid account, DateOnly? date = null)
        {
            var payment = Charge(account, date ?? Start, Bill.ProviderName);
            Db.BillTransactionAssociations.Add(new BillTransactionAssociationEntity
            {
                UserId = Owner, BillStreamId = Bill.Id, BankTransactionId = payment.Id,
                CreatedAtUtc = OriginalTime, UpdatedAtUtc = OriginalTime
            });
            return payment;
        }

        public void AddHistory(Guid account, int days)
        {
            Charge(account, Start, "Unrelated start");
            Charge(account, Start.AddDays(days), "Unrelated end");
        }

        public BankTransactionEntity Charge(Guid account, DateOnly date, string name, Guid? owner = null)
        {
            var transaction = new BankTransactionEntity
            {
                UserId = owner ?? Owner, BankAccountId = account,
                PlaidTransactionId = Guid.NewGuid().ToString("N"), Name = name, MerchantName = name,
                Amount = 20m, IsoCurrencyCode = "USD", PostedDate = date,
                CategoryPrimary = "FOOD_AND_DRINK"
            };
            Db.BankTransactions.Add(transaction);
            return transaction;
        }

        public async Task VerifyAsync(bool canDeactivate, IBankTransactionDiscoveryGateway? gateway = null)
        {
            await Db.SaveChangesAsync();
            var links = await Db.BillTransactionAssociations.AsNoTracking().ToListAsync();
            var transactionFacts = await Db.BankTransactions.AsNoTracking().OrderBy(item => item.Id)
                .Select(item => new { item.Id, item.Amount, item.IsPending, item.IsRemoved, item.PostedDate })
                .ToListAsync();
            Db.ChangeTracker.Clear();
            var service = new RecurringBillDiscoveryPersistenceService(Db,
                gateway ?? new PlaidBankTransactionDiscoveryGateway(Db), new BillTransactionAssociationGateway(Db));
            var result = await service.DiscoverAndSaveAsync(Owner);
            Assert.Equal(canDeactivate ? 1 : 0, result.BillStreamsDeactivated);
            Assert.Equal(canDeactivate ? links.Count : 0, result.TransactionsUnlinked);
            Assert.Equal(0, result.BillsDiscovered);
            Assert.Equal(0, result.NewBillAlertsCreated);
            Db.ChangeTracker.Clear();
            var saved = await Db.BillStreams.SingleAsync();
            Assert.Equal(!canDeactivate, saved.IsActive);
            Assert.Equal(Bill.Source, saved.Source);
            if (!canDeactivate) Assert.Equal(OriginalTime, saved.UpdatedAtUtc);
            var savedLinks = await Db.BillTransactionAssociations.AsNoTracking().ToListAsync();
            Assert.Equal(canDeactivate ? 0 : links.Count, savedLinks.Count);
            if (!canDeactivate)
            {
                Assert.Equal(links.OrderBy(link => link.BankTransactionId).Select(link =>
                    (link.UserId, link.BankTransactionId, link.BillStreamId, link.UpdatedAtUtc)),
                    savedLinks.OrderBy(link => link.BankTransactionId).Select(link =>
                    (link.UserId, link.BankTransactionId, link.BillStreamId, link.UpdatedAtUtc)));
            }
            Assert.Equal(transactionFacts, await Db.BankTransactions.AsNoTracking().OrderBy(item => item.Id)
                .Select(item => new { item.Id, item.Amount, item.IsPending, item.IsRemoved, item.PostedDate })
                .ToListAsync());
            Assert.Empty(Db.BillAlerts);

            // Repeating the same scan cannot erase retained evidence or repeat deactivation.
            var repeat = await service.DiscoverAndSaveAsync(Owner);
            Assert.Equal(0, repeat.BillStreamsDeactivated);
            Assert.Equal(0, repeat.TransactionsUnlinked);
        }

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class MissingAccountGateway(IBankTransactionDiscoveryGateway inner, bool emptyGuid)
        : IBankTransactionDiscoveryGateway
    {
        public async Task<IReadOnlyList<BankTransactionDiscoveryRecord>> GetDiscoveryTransactionsAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            (await inner.GetDiscoveryTransactionsAsync(userId, cancellationToken))
                .Select(record => record with { BankAccountId = emptyGuid ? Guid.Empty : null }).ToArray();
    }
}

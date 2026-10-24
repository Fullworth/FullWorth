using System.Net.Http.Json;
using FullWorth.API.Controllers;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class BankTransactionStableWindowTests
{
    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(501, 500)]
    [InlineData(int.MaxValue, 500)]
    public async Task PopulatedWindow_IsStableAcrossReloads_AndRetainsOwnershipAndRemovalFilters(int take, int expectedCount)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var session = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var other = await TestUserAuthentication.RegisterAndLoginAsync(client);
        TestUserAuthentication.Authorize(client, session);
        var owner = await TestUserAuthentication.GetUserIdAsync(factory, session.Email);
        var foreignOwner = await TestUserAuthentication.GetUserIdAsync(factory, other.Email);
        var date = new DateOnly(2026, 10, 1);
        var created = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
        var expected = new[] { Id(1), Id(2) }.Concat(Enumerable.Range(1001, 525).Reverse().Select(Id))
            .Take(expectedCount).ToArray();
        Guid foreignId;
        Guid removedId;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            var connection = new BankConnectionEntity { UserId = owner, InstitutionName = "Owner bank" };
            var foreignConnection = new BankConnectionEntity { UserId = foreignOwner, InstitutionName = "Foreign bank" };
            var account = new BankAccountEntity
            {
                UserId = owner, BankConnection = connection, BankConnectionId = connection.Id,
                Name = "Owner account", PlaidAccountId = "owner-account"
            };
            var foreignAccount = new BankAccountEntity
            {
                UserId = foreignOwner, BankConnection = foreignConnection, BankConnectionId = foreignConnection.Id,
                Name = "Foreign account", PlaidAccountId = "foreign-account"
            };
            db.BankConnections.AddRange(connection, foreignConnection);
            db.BankAccounts.AddRange(account, foreignAccount);
            // Ascending insertion deliberately differs from the required tie order.
            foreach (var id in Enumerable.Range(1001, 525))
                db.BankTransactions.Add(Transaction(Id(id), owner, account, date, created));
            db.BankTransactions.Add(Transaction(Id(1), owner, account, date.AddDays(1), created.AddDays(-1)));
            db.BankTransactions.Add(Transaction(Id(2), owner, account, date, created.AddMinutes(1)));
            var removed = Transaction(Id(9998), owner, account, date.AddDays(2), created);
            removed.IsRemoved = true;
            var foreign = Transaction(Id(9999), foreignOwner, foreignAccount, date.AddDays(3), created);
            removedId = removed.Id;
            foreignId = foreign.Id;
            db.BankTransactions.AddRange(removed, foreign);
            await db.SaveChangesAsync();
        }

        for (var reload = 0; reload < 3; reload++)
        {
            using var response = await client.GetAsync($"/api/bank-transactions?take={take}");
            response.EnsureSuccessStatusCode();
            var rows = await response.Content.ReadFromJsonAsync<List<BankTransactionResult>>();
            Assert.NotNull(rows);
            Assert.Equal(expected, rows.Select(row => row.Id));
            Assert.DoesNotContain(rows, row => row.Id == foreignId || row.Id == removedId);
            Assert.All(rows, row =>
            {
                Assert.Equal("Owner bank", row.InstitutionName);
                Assert.Equal("Owner account", row.AccountName);
                Assert.Equal(-42.17m, row.Amount);
                Assert.Equal("TRANSFER_IN", row.CategoryPrimary);
                Assert.True(row.IsPending);
            });
        }

        // The other authenticated owner gets only its own row.
        TestUserAuthentication.Authorize(client, other);
        var foreignRows = await client.GetFromJsonAsync<List<BankTransactionResult>>("/api/bank-transactions?take=500");
        Assert.Equal(foreignId, Assert.Single(foreignRows!).Id);

        await using var verification = factory.Services.CreateAsyncScope();
        var stored = verification.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        Assert.Equal(529, await stored.BankTransactions.CountAsync());
        Assert.True((await stored.BankTransactions.SingleAsync(t => t.Id == removedId)).IsRemoved);
        Assert.All(await stored.BankTransactions.AsNoTracking().ToListAsync(), row =>
        {
            Assert.Equal(-42.17m, row.Amount);
            Assert.Equal("TRANSFER_IN", row.CategoryPrimary);
            Assert.True(row.IsPending);
        });
    }

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:000000000000}");
    private static BankTransactionEntity Transaction(Guid id, Guid owner, BankAccountEntity account,
        DateOnly date, DateTimeOffset created) => new()
    {
        Id = id, UserId = owner, BankAccount = account, BankAccountId = account.Id,
        PlaidTransactionId = "synthetic-" + id, Name = "Synthetic transaction",
        Amount = -42.17m, IsoCurrencyCode = "USD", CategoryPrimary = "TRANSFER_IN",
        IsPending = true, PostedDate = date, CreatedAtUtc = created, UpdatedAtUtc = created
    };
}

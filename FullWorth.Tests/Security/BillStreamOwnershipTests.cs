using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.Core.Models;
using FullWorth.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class BillStreamOwnershipTests
{
    [Fact]
    public async Task Detail_ForAnotherUsersStream_ReturnsNotFound()
    {
        await using var factory = new FullWorthApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var attackerClient = factory.CreateHttpsClient();

        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var attacker = await TestUserAuthentication.RegisterAndLoginAsync(attackerClient);
        var ownerUserId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        var streamId = await SeedStreamAsync(factory, ownerUserId);

        attackerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", attacker.AccessToken);

        using var response = await attackerClient.GetAsync($"/api/bill-streams/{streamId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_DoesNotReturnAnotherUsersStreams()
    {
        await using var factory = new FullWorthApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var attackerClient = factory.CreateHttpsClient();

        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var attacker = await TestUserAuthentication.RegisterAndLoginAsync(attackerClient);
        var ownerUserId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        var attackerUserId = await TestUserAuthentication.GetUserIdAsync(factory, attacker.Email);
        var ownerStreamId = await SeedStreamAsync(factory, ownerUserId, "Owner Utility");
        var attackerStreamId = await SeedStreamAsync(factory, attackerUserId, "Attacker Utility");

        attackerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", attacker.AccessToken);

        using var response = await attackerClient.GetAsync("/api/bill-streams?includeInactive=true");
        response.EnsureSuccessStatusCode();
        var streams = await response.Content.ReadFromJsonAsync<List<BillStreamResultDto>>();

        Assert.NotNull(streams);
        Assert.Contains(streams, stream => stream.Id == attackerStreamId);
        Assert.DoesNotContain(streams, stream => stream.Id == ownerStreamId);
    }

    [Fact]
    public async Task Detail_ReturnsLatestAmountAndPreviousAverage()
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();

        var user = await TestUserAuthentication.RegisterAndLoginAsync(client);
        var userId = await TestUserAuthentication.GetUserIdAsync(factory, user.Email);
        var streamId = await SeedStreamAsync(factory, userId, "Metrics Utility");

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
            var account = new BankAccountEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BankConnectionId = Guid.NewGuid(),
                PlaidAccountId = $"metrics-{Guid.NewGuid():N}",
                Name = "Metrics Account",
                AccountType = BankAccountType.Checking,
                IsActive = true
            };

            var connection = new BankConnectionEntity
            {
                Id = account.BankConnectionId,
                UserId = userId,
                InstitutionName = "Metrics Bank",
                Status = BankConnectionStatus.Active
            };

            dbContext.BankConnections.Add(connection);
            dbContext.BankAccounts.Add(account);

            var now = DateTimeOffset.UtcNow;

            var oldestTransaction =
                new BankTransactionEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BankAccountId = account.Id,
                    PlaidTransactionId = $"oldest-{Guid.NewGuid():N}",
                    Name = "Metrics Utility",
                    Amount = 10m,
                    PostedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-2)),
                    CreatedAtUtc = now.AddMonths(-2),
                    UpdatedAtUtc = now.AddMonths(-2)
                };

            var previousTransaction =
                new BankTransactionEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BankAccountId = account.Id,
                    PlaidTransactionId = $"previous-{Guid.NewGuid():N}",
                    Name = "Metrics Utility",
                    Amount = 20m,
                    PostedDate = DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)),
                    CreatedAtUtc = now.AddMonths(-1),
                    UpdatedAtUtc = now.AddMonths(-1)
                };

            var latestTransaction =
                new BankTransactionEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    BankAccountId = account.Id,
                    PlaidTransactionId = $"latest-{Guid.NewGuid():N}",
                    Name = "Metrics Utility",
                    Amount = 40m,
                    PostedDate = DateOnly.FromDateTime(DateTime.UtcNow),
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                };

            var transactions =
                new[]
                {
                    oldestTransaction,
                    previousTransaction,
                    latestTransaction
                };

            dbContext.BankTransactions.AddRange(
                transactions);

            dbContext.BillTransactionAssociations.AddRange(
                transactions.Select(
                    transaction =>
                        new BillTransactionAssociationEntity
                        {
                            UserId = userId,
                            BankTransactionId = transaction.Id,
                            BillStreamId = streamId,
                            CreatedAtUtc = now,
                            UpdatedAtUtc = now
                        }));

            await dbContext.SaveChangesAsync();
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", user.AccessToken);

        using var response = await client.GetAsync($"/api/bill-streams/{streamId}");
        response.EnsureSuccessStatusCode();

        var detail = await response.Content.ReadFromJsonAsync<BillStreamResultDto>();

        Assert.NotNull(detail);
        Assert.Equal(40m, detail.CurrentAmount);
        Assert.Equal(15m, detail.PreviousAverage);
    }

    [Fact]
    public async Task Create_DuplicateProviderName_IsScopedToCurrentUser()
    {
        await using var factory = new FullWorthApiFactory();
        using var ownerClient = factory.CreateHttpsClient();
        using var secondClient = factory.CreateHttpsClient();

        var owner = await TestUserAuthentication.RegisterAndLoginAsync(ownerClient);
        var second = await TestUserAuthentication.RegisterAndLoginAsync(secondClient);
        var ownerUserId = await TestUserAuthentication.GetUserIdAsync(factory, owner.Email);
        await SeedStreamAsync(factory, ownerUserId, "Shared Provider");

        secondClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", second.AccessToken);

        using var response = await secondClient.PostAsJsonAsync(
            "/api/bill-streams",
            new { providerName = "Shared Provider", category = "Utility" });

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<BillStreamResultDto>();
        Assert.NotNull(created);

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var matchingStreams = await dbContext.BillStreams
            .Where(stream => stream.ProviderName == "Shared Provider")
            .ToListAsync();

        Assert.Equal(2, matchingStreams.Count);
        Assert.Contains(matchingStreams, stream => stream.UserId == ownerUserId);
        Assert.Contains(matchingStreams, stream => stream.Id == created.Id && stream.UserId != ownerUserId);
    }

    private static async Task<Guid> SeedStreamAsync(
        FullWorthApiFactory factory,
        Guid userId,
        string providerName = "Private Utility")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FullWorthDbContext>();
        var now = DateTimeOffset.UtcNow;
        var stream = new BillStreamEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProviderName = providerName,
            Category = BillCategory.Utility,
            Source = BillStreamSource.Manual,
            IsActive = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        dbContext.BillStreams.Add(stream);
        await dbContext.SaveChangesAsync();
        return stream.Id;
    }

    private sealed record BillStreamResultDto(
        Guid Id,
        string ProviderName,
        string Category,
        bool IsActive,
        decimal CurrentAmount,
        decimal PreviousAverage);
}

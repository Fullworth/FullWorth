using System.Net;
using System.Text;
using System.Text.Json;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidConnectionSyncCoordinatorTests
{
    [Theory]
    [InlineData("ITEM_LOGIN_REQUIRED")]
    [InlineData("ACCESS_NOT_GRANTED")]
    [InlineData("ITEM_LOCKED")]
    [InlineData("PASSWORD_RESET_REQUIRED")]
    [InlineData("USER_SETUP_REQUIRED")]
    public async Task AccountSync_UserActionItemError_PersistsRequiresAttentionAndPreservesCredential(
        string errorCode)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            "attention-access-token");

        dbContext.BankConnections.Add(connection);
        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                PlaidError(
                    HttpStatusCode.BadRequest,
                    "ITEM_ERROR",
                    errorCode));

        using var httpClient = new HttpClient(handler);
        var coordinator = CreateCoordinator(
            dbContext,
            httpClient,
            tokenProtector);

        var exception =
            await Assert.ThrowsAsync<PlaidApiException>(
                () => coordinator.SyncAccountsAsync(
                    userId,
                    connection.Id));

        Assert.Equal(errorCode, exception.ErrorCode);
        Assert.Equal(
            BankConnectionStatus.RequiresAttention,
            connection.Status);
        Assert.Equal(
            "attention-access-token",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));
        Assert.Equal(
            "cursor-before-error",
            connection.TransactionsCursor);
    }

    [Fact]
    public async Task TransactionSync_LoginRequired_PersistsRequiresAttentionBeforePropagatingError()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            "transaction-attention-token");

        dbContext.BankConnections.Add(connection);
        dbContext.BankAccounts.Add(
            new BankAccountEntity
            {
                UserId = userId,
                BankConnectionId = connection.Id,
                PlaidAccountId = "account-1",
                Name = "Checking",
                IsActive = true
            });

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                PlaidError(
                    HttpStatusCode.BadRequest,
                    "ITEM_ERROR",
                    "ITEM_LOGIN_REQUIRED"));

        using var httpClient = new HttpClient(handler);
        var coordinator = CreateCoordinator(
            dbContext,
            httpClient,
            tokenProtector);

        await Assert.ThrowsAsync<PlaidApiException>(
            () => coordinator.SyncTransactionsAsync(
                userId,
                connection.Id));

        Assert.Equal(
            BankConnectionStatus.RequiresAttention,
            connection.Status);
        Assert.Equal(
            "cursor-before-error",
            connection.TransactionsCursor);
        Assert.Empty(dbContext.BankTransactions);
        Assert.Equal(
            "transaction-attention-token",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));
    }

    [Theory]
    [InlineData("INSTITUTION_ERROR", "INSTITUTION_DOWN")]
    [InlineData("API_ERROR", "INTERNAL_SERVER_ERROR")]
    [InlineData("ITEM_ERROR", "ITEM_NOT_FOUND")]
    public async Task NonUpdateModeErrors_DoNotDisableConnection(
        string errorType,
        string errorCode)
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            "healthy-token");

        dbContext.BankConnections.Add(connection);
        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                PlaidError(
                    HttpStatusCode.BadRequest,
                    errorType,
                    errorCode));

        using var httpClient = new HttpClient(handler);
        var coordinator = CreateCoordinator(
            dbContext,
            httpClient,
            tokenProtector);

        await Assert.ThrowsAsync<PlaidApiException>(
            () => coordinator.SyncAccountsAsync(
                userId,
                connection.Id));

        Assert.Equal(
            BankConnectionStatus.Active,
            connection.Status);
        Assert.Equal(
            "cursor-before-error",
            connection.TransactionsCursor);
        Assert.Equal(
            "healthy-token",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));
    }

    [Fact]
    public async Task SyncAll_StopsRetryingConnectionAfterItRequiresUserAttention()
    {
        await using var dbContext = CreateDbContext();
        var userId = Guid.NewGuid();
        var tokenProtector = CreateTokenProtector();
        var connection = CreateConnection(
            userId,
            tokenProtector,
            "attention-once-token");

        dbContext.BankConnections.Add(connection);
        await dbContext.SaveChangesAsync();

        using var firstHandler =
            new ScriptedHttpMessageHandler(
                PlaidError(
                    HttpStatusCode.BadRequest,
                    "ITEM_ERROR",
                    "ITEM_LOGIN_REQUIRED"));

        using var firstHttpClient = new HttpClient(firstHandler);
        var firstCoordinator = CreateCoordinator(
            dbContext,
            firstHttpClient,
            tokenProtector);

        await Assert.ThrowsAsync<PlaidApiException>(
            () => firstCoordinator.SyncAllAccountsAsync(userId));

        Assert.Equal(
            BankConnectionStatus.RequiresAttention,
            connection.Status);

        using var secondHandler =
            new ScriptedHttpMessageHandler();

        using var secondHttpClient = new HttpClient(secondHandler);
        var secondCoordinator = CreateCoordinator(
            dbContext,
            secondHttpClient,
            tokenProtector);

        var secondResult =
            await secondCoordinator.SyncAllAccountsAsync(userId);

        Assert.Equal(0, secondResult.ConnectionsSynced);
        Assert.Equal(0, secondResult.AccountsSynced);
        Assert.Empty(secondHandler.Requests);
    }

    [Fact]
    public async Task SyncAllAccounts_MultipleConnections_SyncsEachOwnedConnection()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            CreateTokenProtector();

        var firstConnection =
            CreateConnection(
                userId,
                tokenProtector,
                "access-token-one");

        firstConnection.Id =
            Guid.Parse(
                "00000000-0000-0000-0000-000000000001");

        firstConnection.PlaidItemId =
            "item-one";

        firstConnection.InstitutionName =
            "Bank One";

        var secondConnection =
            CreateConnection(
                userId,
                tokenProtector,
                "access-token-two");

        secondConnection.Id =
            Guid.Parse(
                "00000000-0000-0000-0000-000000000002");

        secondConnection.PlaidItemId =
            "item-two";

        secondConnection.InstitutionName =
            "Bank Two";

        dbContext.BankConnections.AddRange(
            firstConnection,
            secondConnection);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                AccountResponse(
                    "account-one",
                    "Checking One"),
                AccountResponse(
                    "account-two",
                    "Checking Two"));

        using var httpClient =
            new HttpClient(
                handler);

        var coordinator =
            CreateCoordinator(
                dbContext,
                httpClient,
                tokenProtector);

        var result =
            await coordinator.SyncAllAccountsAsync(
                userId);

        Assert.Equal(
            2,
            result.ConnectionsSynced);

        Assert.Equal(
            2,
            result.AccountsSynced);

        Assert.Equal(
            2,
            handler.Requests.Count);

        Assert.Contains(
            "\"access_token\":\"access-token-one\"",
            handler.Requests[0].Body,
            StringComparison.Ordinal);

        Assert.Contains(
            "\"access_token\":\"access-token-two\"",
            handler.Requests[1].Body,
            StringComparison.Ordinal);

        var accounts =
            await dbContext.BankAccounts
                .OrderBy(
                    account =>
                        account.PlaidAccountId)
                .ToListAsync();

        Assert.Equal(
            2,
            accounts.Count);

        Assert.Contains(
            accounts,
            account =>
                account.PlaidAccountId ==
                    "account-one" &&
                account.BankConnectionId ==
                    firstConnection.Id);

        Assert.Contains(
            accounts,
            account =>
                account.PlaidAccountId ==
                    "account-two" &&
                account.BankConnectionId ==
                    secondConnection.Id);

        Assert.NotNull(
            firstConnection.LastSuccessfulSyncAtUtc);

        Assert.NotNull(
            secondConnection.LastSuccessfulSyncAtUtc);
    }

    private static PlaidConnectionSyncCoordinator CreateCoordinator(
        FullWorthDbContext dbContext,
        HttpClient httpClient,
        PlaidTokenProtector tokenProtector)
    {
        var apiClient =
            new PlaidApiClient(
                httpClient,
                Options.Create(
                    new PlaidOptions
                    {
                        ClientId = "test-client-id",
                        Secret = "test-secret",
                        Environment = PlaidOptions.SandboxEnvironment
                    }));

        return new PlaidConnectionSyncCoordinator(
            dbContext,
            new PlaidAccountSyncService(
                dbContext,
                apiClient,
                tokenProtector),
            new PlaidTransactionSyncService(
                dbContext,
                apiClient,
                tokenProtector));
    }

    private static BankConnectionEntity CreateConnection(
        Guid userId,
        PlaidTokenProtector tokenProtector,
        string plaintextAccessToken)
    {
        return new BankConnectionEntity
        {
            UserId = userId,
            InstitutionName = "Attention Test Bank",
            PlaidItemId = "item-attention-test",
            ProtectedPlaidAccessToken =
                tokenProtector.Protect(plaintextAccessToken),
            TransactionsCursor = "cursor-before-error",
            Status = BankConnectionStatus.Active
        };
    }

    private static PlaidTokenProtector CreateTokenProtector() =>
        new(
            new EphemeralDataProtectionProvider());

    private static FullWorthDbContext CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                .Options);
    }

    private static HttpResponseMessage AccountResponse(
        string accountId,
        string accountName)
    {
        var json =
            JsonSerializer.Serialize(
                new
                {
                    accounts =
                        new[]
                        {
                            new
                            {
                                account_id =
                                    accountId,

                                name =
                                    accountName,

                                official_name =
                                    (string?)null,

                                mask =
                                    "1234",

                                type =
                                    "depository",

                                subtype =
                                    "checking"
                            }
                        },

                    request_id =
                        "safe-test-request-id"
                });

        return new HttpResponseMessage(
            HttpStatusCode.OK)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private static HttpResponseMessage PlaidError(
        HttpStatusCode statusCode,
        string errorType,
        string errorCode)
    {
        var json =
            $$"""
            {
              "error_type": "{{errorType}}",
              "error_code": "{{errorCode}}",
              "request_id": "safe-test-request-id"
            }
            """;

        return new HttpResponseMessage(statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }
}

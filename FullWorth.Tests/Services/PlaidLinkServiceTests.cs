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

public sealed class PlaidLinkServiceTests
{
    [Fact]
    public async Task InitialMode_RequestsMaximumTransactionHistory()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_token": "link-sandbox-initial",
                      "hosted_link_url": "https://secure.plaid.com/initial-test",
                      "expiration": "2099-08-30T18:00:00Z"
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var service =
            new PlaidLinkService(
                CreateApiClient(
                    httpClient),
                tokenProtector,
                dbContext);

        var result =
            await service.CreateLinkSessionAsync(
                userId);

        Assert.NotEqual(
            Guid.Empty,
            result.SessionId);

        var request =
            Assert.Single(
                handler.Requests);

        using var requestJson =
            JsonDocument.Parse(
                request.Body);

        var root =
            requestJson.RootElement;

        Assert.Equal(
            "transactions",
            Assert.Single(
                    root.GetProperty(
                            "products")
                        .EnumerateArray())
                .GetString());

        Assert.Equal(
            730,
            root.GetProperty(
                    "transactions")
                .GetProperty(
                    "days_requested")
                .GetInt32());

        Assert.False(
            root.TryGetProperty(
                "access_token",
                out _));

        var session =
            await dbContext.PlaidLinkSessions
                .SingleAsync();

        Assert.Null(
            session.BankConnectionId);
    }

    [Fact]
    public async Task UpdateMode_IsOwnershipScopedAndUsesExistingProtectedAccessToken()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var connection =
            new BankConnectionEntity
            {
                UserId =
                    userId,

                InstitutionName =
                    "Test Bank",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-sandbox-update"),

                Status =
                    BankConnectionStatus.RequiresAttention
            };

        dbContext.BankConnections.Add(
            connection);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_token": "link-sandbox-update",
                      "hosted_link_url": "https://secure.plaid.com/update-test",
                      "expiration": "2099-08-30T18:00:00Z"
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var service =
            new PlaidLinkService(
                CreateApiClient(
                    httpClient),
                tokenProtector,
                dbContext);

        var result =
            await service.CreateLinkSessionAsync(
                userId,
                connection.Id);

        Assert.NotEqual(
            Guid.Empty,
            result.SessionId);

        var request =
            Assert.Single(
                handler.Requests);

        using var requestJson =
            JsonDocument.Parse(
                request.Body);

        Assert.Equal(
            "access-sandbox-update",
            requestJson.RootElement
                .GetProperty(
                    "access_token")
                .GetString());

        Assert.False(
            requestJson.RootElement.TryGetProperty(
                "products",
                out _));

        Assert.False(
            requestJson.RootElement.TryGetProperty(
                "transactions",
                out _));

        var session =
            await dbContext.PlaidLinkSessions
                .SingleAsync();

        Assert.Equal(
            connection.Id,
            session.BankConnectionId);

        Assert.NotEqual(
            "link-sandbox-update",
            session.ProtectedLinkToken);

        Assert.Equal(
            "link-sandbox-update",
            tokenProtector.UnprotectLinkToken(
                session.ProtectedLinkToken));
    }

    [Fact]
    public async Task UpdateMode_OtherUsersConnection_IsNotFoundBeforeProviderCall()
    {
        await using var dbContext =
            CreateDbContext();

        var ownerId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var connection =
            new BankConnectionEntity
            {
                UserId =
                    ownerId,

                InstitutionName =
                    "Owner Bank",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-owner"),

                Status =
                    BankConnectionStatus.RequiresAttention
            };

        dbContext.BankConnections.Add(
            connection);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler();

        using var httpClient =
            new HttpClient(
                handler);

        var service =
            new PlaidLinkService(
                CreateApiClient(
                    httpClient),
                tokenProtector,
                dbContext);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.CreateLinkSessionAsync(
                Guid.NewGuid(),
                connection.Id));

        Assert.Empty(
            handler.Requests);

        Assert.Empty(
            dbContext.PlaidLinkSessions);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("missing")]
    public async Task LinkSessionExpiration_IsBoundedByProviderAndLocalRetention(string mode)
    {
        await using var db = CreateDbContext();
        var protector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var owner = Guid.NewGuid();
        var start = DateTimeOffset.UtcNow;
        var providerExpiration = mode == "short"
            ? start.AddMinutes(20)
            : mode == "long"
                ? start.AddDays(90)
                : (DateTimeOffset?)null;
        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK,
                JsonSerializer.Serialize(new
                {
                    link_token = "expiry-test-link",
                    hosted_link_url = "https://secure.plaid.com/session",
                    expiration = providerExpiration?.ToString("O")
                })));
        using var http = new HttpClient(handler);
        var service = new PlaidLinkService(CreateApiClient(http), protector, db);

        await service.CreateLinkSessionAsync(owner);

        var session = await db.PlaidLinkSessions.AsNoTracking().SingleAsync();
        Assert.Equal(owner, session.UserId);
        Assert.Equal(PlaidLinkSessionStatus.Pending, session.Status);
        Assert.InRange(session.ExpiresAtUtc,
            mode == "short" ? start.AddMinutes(19) : start.AddHours(3).AddMinutes(59),
            mode == "short" ? start.AddMinutes(21) : start.AddHours(4).AddSeconds(3));
        if (providerExpiration.HasValue)
            Assert.True(session.ExpiresAtUtc <= providerExpiration.Value);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(5000)]
    [InlineData(7000)]
    public async Task ProviderLinkTokenMustFitEncryptedDatabaseColumn_BeforeTrackingSession(
        int rawLength)
    {
        await using var db = CreateDbContext();
        var protector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var providerToken = new string('z', rawLength);
        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK,
                JsonSerializer.Serialize(new
                {
                    link_token = providerToken,
                    hosted_link_url = "https://secure.plaid.com/test",
                    expiration = DateTimeOffset.UtcNow.AddMinutes(20).ToString("O")
                })));
        using var http = new HttpClient(handler);
        var service = new PlaidLinkService(CreateApiClient(http), protector, db);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateLinkSessionAsync(Guid.NewGuid()));

        Assert.Contains("too large for secure storage", error.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(providerToken, error.Message,
            StringComparison.Ordinal);
        Assert.Empty(await db.PlaidLinkSessions.ToListAsync());
        Assert.DoesNotContain(db.ChangeTracker.Entries<PlaidLinkSessionEntity>(),
            entry => entry.State == EntityState.Added);
        Assert.Collection(handler.Requests,
            request => Assert.Equal("/link/token/create", request.Uri.AbsolutePath));
    }

    [Fact]
    public void LinkCredentialColumnBoundMatchesModel()
    {
        using var db = CreateDbContext();
        var entity = db.Model.FindEntityType(typeof(PlaidLinkSessionEntity));
        Assert.NotNull(entity);
        var property = entity.FindProperty(nameof(PlaidLinkSessionEntity.ProtectedLinkToken));
        Assert.NotNull(property);
        Assert.Equal(PlaidLinkSessionEntity.MaxProtectedLinkTokenLength,
            property.GetMaxLength());
    }

    private static PlaidApiClient CreateApiClient(
        HttpClient httpClient)
    {
        return new PlaidApiClient(
            httpClient,
            Options.Create(
                new PlaidOptions
                {
                    ClientId =
                        "test-client-id",

                    Secret =
                        "test-secret",

                    Environment =
                        PlaidOptions.SandboxEnvironment
                }));
    }

    private static FullWorthDbContext CreateDbContext()
    {
        return new FullWorthDbContext(
            new DbContextOptionsBuilder<FullWorthDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid()
                        .ToString(
                            "N"))
                .Options);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        return new HttpResponseMessage(
            statusCode)
        {
            Content =
                new StringContent(
                    json,
                    Encoding.UTF8,
                    "application/json")
        };
    }
}

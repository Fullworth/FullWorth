using System.Net;
using System.Text;
using FullWorth.API.Data;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Plaid;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Services;

public sealed class PlaidHostedLinkCompletionServiceTests
{
    [Fact]
    public async Task CompletedHostedLink_ExchangesTokenServerSideAndClearsSessionCredential()
    {
        var observer = new CombinedInitialLinkSaveObserver();
        await using var dbContext =
            CreateDbContext(observer);

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var session =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                ProtectedLinkToken =
                    tokenProtector.ProtectLinkToken(
                        "link-sandbox-test"),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    DateTimeOffset.UtcNow.AddHours(
                        1)
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_sessions": [
                        {
                          "finished_at": "2026-08-30T18:00:00Z",
                          "results": {
                            "item_add_results": [
                              { "public_token": "public-sandbox-test" }
                            ]
                          }
                        }
                      ]
                    }
                    """),
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "access_token": "access-sandbox-test",
                      "item_id": "item-test-1"
                    }
                    """),
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "item": {
                        "item_id": "item-test-1",
                        "institution_id": "ins_test",
                        "institution_name": "Test Bank"
                      }
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var plaidApiClient =
            CreateApiClient(
                httpClient);

        var exchangeService =
            new PlaidConnectionExchangeService(
                plaidApiClient,
                tokenProtector,
                dbContext);

        var service =
            new PlaidHostedLinkCompletionService(
                dbContext,
                plaidApiClient,
                tokenProtector,
                exchangeService);

        var result =
            await service.CheckAndCompleteAsync(
                userId,
                session.Id);

        Assert.Equal(
            "Completed",
            result.Status);

        Assert.NotNull(
            result.Connection);

        Assert.Equal(
            "Test Bank",
            result.Connection.InstitutionName);

        Assert.Equal(
            string.Empty,
            session.ProtectedLinkToken);

        var connection =
            await dbContext.BankConnections
                .SingleAsync();

        Assert.NotEqual(
            "access-sandbox-test",
            connection.ProtectedPlaidAccessToken);

        Assert.Equal(
            "access-sandbox-test",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));

        // Terminal session and newly added bank must share one database write.
        Assert.Equal(connection.Id, session.BankConnectionId);
        Assert.Equal(1, observer.CombinedWrites);

        Assert.Collection(
            handler.Requests,
            request =>
                Assert.Equal(
                    "/link/token/get",
                    request.Uri.AbsolutePath),
            request =>
                Assert.Equal(
                    "/item/public_token/exchange",
                    request.Uri.AbsolutePath),
            request =>
                Assert.Equal(
                    "/item/get",
                    request.Uri.AbsolutePath));

        var replay = await service.CheckAndCompleteAsync(userId, session.Id);
        Assert.Equal("Completed", replay.Status);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(1, observer.CombinedWrites);
    }

    [Fact]
    public async Task ExitedHostedLink_BecomesTerminalWithoutExchangingToken()
    {
        await using var dbContext =
            CreateDbContext();

        var userId =
            Guid.NewGuid();

        var tokenProtector =
            new PlaidTokenProtector(
                new EphemeralDataProtectionProvider());

        var session =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                ProtectedLinkToken =
                    tokenProtector.ProtectLinkToken(
                        "link-sandbox-exited"),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    DateTimeOffset.UtcNow.AddHours(
                        1)
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_sessions": [
                        {
                          "finished_at": "2026-08-30T18:00:00Z",
                          "exit": {}
                        }
                      ]
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var plaidApiClient =
            CreateApiClient(
                httpClient);

        var service =
            new PlaidHostedLinkCompletionService(
                dbContext,
                plaidApiClient,
                tokenProtector,
                new PlaidConnectionExchangeService(
                    plaidApiClient,
                    tokenProtector,
                    dbContext));

        var result =
            await service.CheckAndCompleteAsync(
                userId,
                session.Id);

        Assert.Equal(
            "Exited",
            result.Status);

        Assert.Null(
            result.Connection);

        Assert.Equal(
            string.Empty,
            session.ProtectedLinkToken);

        Assert.Single(
            handler.Requests);

        Assert.Empty(
            dbContext.BankConnections);
    }

    [Fact]
    public async Task CompletedUpdateMode_ReactivatesOwnedConnectionWithoutExchangingToken()
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
                    "Update Bank",

                PlaidItemId =
                    "item-update-1",

                ProtectedPlaidAccessToken =
                    tokenProtector.Protect(
                        "access-update-1"),

                Status =
                    BankConnectionStatus.RequiresAttention
            };

        dbContext.BankConnections.Add(
            connection);

        var session =
            new PlaidLinkSessionEntity
            {
                UserId =
                    userId,

                BankConnectionId =
                    connection.Id,

                ProtectedLinkToken =
                    tokenProtector.ProtectLinkToken(
                        "link-sandbox-update"),

                Status =
                    PlaidLinkSessionStatus.Pending,

                ExpiresAtUtc =
                    DateTimeOffset.UtcNow.AddMinutes(
                        30)
            };

        dbContext.PlaidLinkSessions.Add(
            session);

        await dbContext.SaveChangesAsync();

        using var handler =
            new ScriptedHttpMessageHandler(
                JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "link_sessions": [
                        {
                          "finished_at": "2026-08-30T18:00:00Z",
                          "results": {}
                        }
                      ]
                    }
                    """));

        using var httpClient =
            new HttpClient(
                handler);

        var plaidApiClient =
            CreateApiClient(
                httpClient);

        var service =
            new PlaidHostedLinkCompletionService(
                dbContext,
                plaidApiClient,
                tokenProtector,
                new PlaidConnectionExchangeService(
                    plaidApiClient,
                    tokenProtector,
                    dbContext));

        var result =
            await service.CheckAndCompleteAsync(
                userId,
                session.Id);

        Assert.Equal(
            "Completed",
            result.Status);

        Assert.Equal(
            "Active",
            result.Connection?.Status);

        Assert.Equal(
            BankConnectionStatus.Active,
            connection.Status);

        Assert.Equal(
            string.Empty,
            session.ProtectedLinkToken);

        Assert.Single(
            handler.Requests);

        Assert.Equal(
            "access-update-1",
            tokenProtector.Unprotect(
                connection.ProtectedPlaidAccessToken!));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MixedHostedSessions_DoNotExchangePublicTokenWhileAnySessionIsUnfinished(
        bool completedSessionFirst)
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var tokenProtector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var protectedLinkToken = tokenProtector.ProtectLinkToken(
            "link-mixed-provider-sessions");
        var session = new PlaidLinkSessionEntity
        {
            UserId = userId,
            ProtectedLinkToken = protectedLinkToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.PlaidLinkSessions.Add(session);
        await db.SaveChangesAsync();

        // Both arrays describe the same provider state. The first finished
        // session has a public token, but another session is not finished.
        var response = completedSessionFirst
            ? """
              {"link_sessions":[
                {"finished_at":"2026-08-30T18:00:00Z",
                 "results":{"item_add_results":[{"public_token":"public-mixed-token"}]}},
                {"results":{}}
              ]}
              """
            : """
              {"link_sessions":[
                {"results":{}},
                {"finished_at":"2026-08-30T18:00:00Z",
                 "results":{"item_add_results":[{"public_token":"public-mixed-token"}]}}
              ]}
              """;
        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, response));
        using var http = new HttpClient(handler);
        var api = CreateApiClient(http);
        var service = new PlaidHostedLinkCompletionService(
            db, api, tokenProtector,
            new PlaidConnectionExchangeService(api, tokenProtector, db));

        var result = await service.CheckAndCompleteAsync(userId, session.Id);

        Assert.Equal("Pending", result.Status);
        Assert.Null(result.Connection);
        Assert.Equal(PlaidLinkSessionStatus.Pending, session.Status);
        Assert.Equal(protectedLinkToken, session.ProtectedLinkToken);
        Assert.Null(session.BankConnectionId);
        Assert.Empty(await db.BankConnections.ToListAsync());
        Assert.Collection(handler.Requests,
            request => Assert.Equal("/link/token/get", request.Uri.AbsolutePath));
    }

    [Fact]
    public async Task FinishedProviderSession_DoesNotHideMalformedLaterSession()
    {
        await using var db = CreateDbContext();
        var userId = Guid.NewGuid();
        var tokenProtector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var protectedToken = tokenProtector.ProtectLinkToken("link-malformed-later");
        var session = new PlaidLinkSessionEntity
        {
            UserId = userId,
            ProtectedLinkToken = protectedToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.PlaidLinkSessions.Add(session);
        await db.SaveChangesAsync();

        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK,
                """
                {"link_sessions":[
                  {"finished_at":"2026-08-30T18:00:00Z",
                   "results":{"item_add_results":[{"public_token":"public-first"}]}},
                  42
                ]}
                """));
        using var http = new HttpClient(handler);
        var api = CreateApiClient(http);
        var service = new PlaidHostedLinkCompletionService(
            db, api, tokenProtector,
            new PlaidConnectionExchangeService(api, tokenProtector, db));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAndCompleteAsync(userId, session.Id));

        Assert.Equal(PlaidLinkSessionStatus.Pending, session.Status);
        Assert.Equal(protectedToken, session.ProtectedLinkToken);
        Assert.Empty(await db.BankConnections.ToListAsync());
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("conflicting-sessions")]
    [InlineData("conflicting-fields")]
    [InlineData("conflicting-item-add-results")]
    [InlineData("malformed-trailing-results")]
    public async Task HostedLinkMultipleTokenSources_RejectsConflictingOrMalformedEvidence(
        string mode)
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var protector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var originalLinkToken = protector.ProtectLinkToken("link-token-conflicts");
        var session = new PlaidLinkSessionEntity
        {
            UserId = owner,
            ProtectedLinkToken = originalLinkToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.PlaidLinkSessions.Add(session);
        await db.SaveChangesAsync();

        var body = mode switch
        {
            "conflicting-sessions" => """
                {"link_sessions":[
                  {"finished_at":"2026-08-30T18:00:00Z",
                   "results":{"item_add_results":[{"public_token":"token-one"}]}},
                  {"finished_at":"2026-08-30T18:01:00Z",
                   "results":{"item_add_results":[{"public_token":"token-two"}]}}
                ]}
                """,
            "conflicting-fields" => """
                {"link_sessions":[{
                  "finished_at":"2026-08-30T18:00:00Z",
                  "results":{"item_add_results":[{"public_token":"token-one"}]},
                  "on_success":{"public_token":"token-two"}
                }]}
                """,
            "conflicting-item-add-results" => """
                {"link_sessions":[{
                  "finished_at":"2026-08-30T18:00:00Z",
                  "results":{"item_add_results":[
                    {"public_token":"token-one"},
                    {"public_token":"token-two"}
                  ]}
                }]}
                """,
            _ => """
                {"link_sessions":[
                  {"finished_at":"2026-08-30T18:00:00Z",
                   "results":{"item_add_results":[{"public_token":"token-one"}]}},
                  {"finished_at":"2026-08-30T18:01:00Z",
                   "results":{"item_add_results":42}}
                ]}
                """
        };
        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, body));
        using var http = new HttpClient(handler);
        var api = CreateApiClient(http);
        var service = new PlaidHostedLinkCompletionService(
            db, api, protector,
            new PlaidConnectionExchangeService(api, protector, db));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CheckAndCompleteAsync(owner, session.Id));

        Assert.Equal(PlaidLinkSessionStatus.Pending, session.Status);
        Assert.Equal(originalLinkToken, session.ProtectedLinkToken);
        Assert.Null(session.BankConnectionId);
        Assert.Empty(await db.BankConnections.ToListAsync());
        Assert.Collection(handler.Requests,
            request => Assert.Equal("/link/token/get", request.Uri.AbsolutePath));
    }

    [Theory]
    [InlineData("exit-first")]
    [InlineData("token-first")]
    [InlineData("exit-with-token")]
    public async Task InitialHostedLink_ExitedProviderEvidenceNeverExchangesPublicToken(
        string mode)
    {
        await using var db = CreateDbContext();
        var owner = Guid.NewGuid();
        var protector = new PlaidTokenProtector(
            new EphemeralDataProtectionProvider());
        var session = new PlaidLinkSessionEntity
        {
            UserId = owner,
            ProtectedLinkToken = protector.ProtectLinkToken("link-exit-conflict"),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1)
        };
        db.PlaidLinkSessions.Add(session);
        await db.SaveChangesAsync();

        var providerState = mode switch
        {
            "exit-first" => """
                {"link_sessions":[
                    {"finished_at":"2026-08-30T18:00:00Z","exit":{}},
                    {"finished_at":"2026-08-30T18:01:00Z",
                     "results":{"item_add_results":[{"public_token":"public-exited"}]}}
                ]}
                """,
            "token-first" => """
                {"link_sessions":[
                    {"finished_at":"2026-08-30T18:00:00Z",
                     "results":{"item_add_results":[{"public_token":"public-exited"}]}},
                    {"finished_at":"2026-08-30T18:01:00Z","exit":{}}
                ]}
                """,
            _ => """
                {"link_sessions":[{
                    "finished_at":"2026-08-30T18:00:00Z","exit":{},
                    "results":{"item_add_results":[{"public_token":"public-exited"}]}
                }]}
                """
        };
        using var handler = new ScriptedHttpMessageHandler(
            JsonResponse(HttpStatusCode.OK, providerState));
        using var http = new HttpClient(handler);
        var api = CreateApiClient(http);
        var service = new PlaidHostedLinkCompletionService(
            db, api, protector,
            new PlaidConnectionExchangeService(api, protector, db));

        var result = await service.CheckAndCompleteAsync(owner, session.Id);

        Assert.Equal("Exited", result.Status);
        Assert.Null(result.Connection);
        Assert.Equal(PlaidLinkSessionStatus.Exited, session.Status);
        Assert.Equal(string.Empty, session.ProtectedLinkToken);
        Assert.Null(session.BankConnectionId);
        Assert.Empty(await db.BankConnections.ToListAsync());
        Assert.Collection(handler.Requests,
            request => Assert.Equal("/link/token/get", request.Uri.AbsolutePath));
        var repeated = await service.CheckAndCompleteAsync(owner, session.Id);
        Assert.Equal("Exited", repeated.Status);
        Assert.Single(handler.Requests);
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

    private sealed class CombinedInitialLinkSaveObserver : SaveChangesInterceptor
    {
        public int CombinedWrites { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var context = eventData.Context!;
            var sessions = context.ChangeTracker.Entries<PlaidLinkSessionEntity>()
                .Where(entry => entry.State == EntityState.Modified &&
                    entry.Entity.Status == PlaidLinkSessionStatus.Completed)
                .ToArray();

            if (sessions.Length > 0)
            {
                var session = Assert.Single(sessions).Entity;
                Assert.Contains(context.ChangeTracker.Entries<BankConnectionEntity>(),
                    entry => entry.State == EntityState.Added &&
                        entry.Entity.Id == session.BankConnectionId &&
                        entry.Entity.UserId == session.UserId);
                CombinedWrites++;
            }
            return ValueTask.FromResult(result);
        }
    }

    private static FullWorthDbContext CreateDbContext(
        SaveChangesInterceptor? observer = null)
    {
        var builder = new DbContextOptionsBuilder<FullWorthDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"));

        if (observer is not null)
            builder.AddInterceptors(observer);

        return new FullWorthDbContext(builder.Options);
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

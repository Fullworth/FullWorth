using FullWorth.API;
using FullWorth.API.Data;
using FullWorth.API.Services.Identity;
using FullWorth.API.Services.Statements;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FullWorth.Tests.Infrastructure;

public sealed class FullWorthApiFactory
    : WebApplicationFactory<ApiAssemblyMarker>
{
    private readonly bool _subscriptionEnforcementEnabled;
    private readonly bool _stripeBillingConfigured;
    private readonly IExternalIdentityTokenValidator?
        _externalIdentityTokenValidator;

    public FullWorthApiFactory()
    {
    }

    private FullWorthApiFactory(
        bool subscriptionEnforcementEnabled,
        bool stripeBillingConfigured,
        IExternalIdentityTokenValidator?
            externalIdentityTokenValidator = null)
    {
        _subscriptionEnforcementEnabled =
            subscriptionEnforcementEnabled;

        _stripeBillingConfigured =
            stripeBillingConfigured;

        _externalIdentityTokenValidator =
            externalIdentityTokenValidator;
    }

    public static FullWorthApiFactory WithSubscriptionEnforcement()
    {
        return new FullWorthApiFactory(
            subscriptionEnforcementEnabled: true,
            stripeBillingConfigured: false);
    }

    public static FullWorthApiFactory WithStripeBilling()
    {
        return new FullWorthApiFactory(
            subscriptionEnforcementEnabled: false,
            stripeBillingConfigured: true);
    }

    public static FullWorthApiFactory WithExternalIdentityValidator(
        IExternalIdentityTokenValidator validator)
    {
        ArgumentNullException.ThrowIfNull(
            validator);

        return new FullWorthApiFactory(
            subscriptionEnforcementEnabled: false,
            stripeBillingConfigured: false,
            externalIdentityTokenValidator:
                validator);
    }

    private readonly string _databaseName =
        $"FullWorthSecurityTests-{Guid.NewGuid():N}";

    private readonly string _statementStorageRoot =
        Path.Combine(
            Path.GetTempPath(),
            "FullWorth.Tests",
            Guid.NewGuid().ToString("N"));

    public HttpClient CreateHttpsClient()
    {
        return CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress =
                    new Uri(
                        "https://localhost"),

                AllowAutoRedirect =
                    false
            });
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        /*
         * Program reads the connection string while constructing the app.
         * Set this at the web-host layer as well as in app configuration so
         * tests never depend on developer user secrets.
         */
        builder.UseSetting(
            "ConnectionStrings:BillWatchDatabase",
            "Host=localhost;Database=billwatch_tests;Username=test;Password=test");

        builder.UseEnvironment(
            "Development");

        builder.ConfigureAppConfiguration(
            (_, configuration) =>
            {
                var testSettings =
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:BillWatchDatabase"] =
                            "Host=localhost;Database=billwatch_tests;Username=test;Password=test",

                        ["Plaid:ClientId"] =
                            "test-client",

                        ["Plaid:Secret"] =
                            "test-secret",

                        ["Plaid:Environment"] =
                            "sandbox",

                        ["BillStatementStorage:RootPath"] =
                            _statementStorageRoot,

                        ["BillStatementStorage:MaxFileSizeBytes"] =
                            (15L * 1024 * 1024)
                                .ToString(),

                        /*
                         * Never let integration tests launch the real
                         * scheduled Plaid monitoring worker.
                         */
                        ["BillMonitoring:BackgroundRefresh:Enabled"] =
                            "false",

                        ["Subscription:EnforcementEnabled"] =
                            _subscriptionEnforcementEnabled.ToString(),

                        ["StripeBilling:Enabled"] =
                            _stripeBillingConfigured.ToString(),

                        ["StripeBilling:SecretKey"] =
                            _stripeBillingConfigured
                                ? "sk_test_billwatch"
                                : string.Empty,

                        ["StripeBilling:WebhookSecret"] =
                            _stripeBillingConfigured
                                ? "whsec_billwatch_test"
                                : string.Empty,

                        ["StripeBilling:MonthlyPriceId"] =
                            _stripeBillingConfigured
                                ? "price_billwatch_monthly_test"
                                : string.Empty,

                        ["StripeBilling:YearlyPriceId"] =
                            _stripeBillingConfigured
                                ? "price_billwatch_yearly_test"
                                : string.Empty,

                        ["StripeBilling:PublicWebBaseUrl"] =
                            "https://billbeacon.net"
                    };

                configuration.AddInMemoryCollection(
                    testSettings);
            });

        builder.ConfigureServices(
            services =>
            {
                services.RemoveAll<
                    IDbContextOptionsConfiguration<
                        FullWorthDbContext>>();

                services.RemoveAll<
                    DbContextOptions<
                        FullWorthDbContext>>();

                services.RemoveAll<
                    FullWorthDbContext>();

                services.AddDbContext<
                    FullWorthDbContext>(
                    options =>
                        options.UseInMemoryDatabase(
                            _databaseName));

                /*
                 * Routine tests do not load native Tesseract.
                 *
                 * Native OCR tests explicitly replace this fake with
                 * the production engine.
                 */
                services.RemoveAll<
                    IBillStatementOcrEngine>();

                services.AddSingleton<
                    IBillStatementOcrEngine,
                    TestBillStatementOcrEngine>();

                if (_externalIdentityTokenValidator is not null)
                {
                    services.RemoveAll<
                        IExternalIdentityTokenValidator>();

                    services.AddSingleton(
                        _externalIdentityTokenValidator);
                }
            });
    }

    protected override void Dispose(
        bool disposing)
    {
        base.Dispose(
            disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (Directory.Exists(
                    _statementStorageRoot))
            {
                Directory.Delete(
                    _statementStorageRoot,
                    recursive:
                        true);
            }
        }
        catch
        {
            // Test cleanup must not hide a real test failure.
        }
    }

    private sealed class TestBillStatementOcrEngine
        : IBillStatementOcrEngine
    {
        public BillStatementOcrResult TryExtract(
            Stream source,
            string mediaType,
            string fileExtension)
        {
            ArgumentNullException.ThrowIfNull(
                source);

            return BillStatementOcrResult.Failure(
                pageCount:
                    1);
        }
    }
}

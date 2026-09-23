using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using FullWorth.Core.Legal;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class ExternalIdentitySecurityTests
{
    private const string TestPassword =
        "FullWorth!Tests123";

    [Fact]
    public async Task ExternalLogin_UnconfiguredProvider_FailsClosed()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/login",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token"
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalRegister_UnconfiguredProvider_FailsClosed()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/register",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    password =
                        TestPassword,

                    acceptedTermsAndPrivacy =
                        true,

                    legalTermsVersion =
                        FullWorthLegalDocuments.CurrentVersion
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalRegister_RequiresCurrentLegalAcceptance()
    {
        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                new FixedExternalIdentityTokenValidator(
                    new ExternalIdentity(
                        ExternalIdentityProviders.Google,
                        "google-subject-legal",
                        "external-legal@fullworth.local",
                        EmailVerified:
                            true)));

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/register",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "valid-test-token",

                    password =
                        TestPassword,

                    acceptedTermsAndPrivacy =
                        false,

                    legalTermsVersion =
                        FullWorthLegalDocuments.CurrentVersion
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalRegister_RequiresProviderVerifiedEmail()
    {
        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                new FixedExternalIdentityTokenValidator(
                    new ExternalIdentity(
                        ExternalIdentityProviders.Apple,
                        "apple-subject-unverified",
                        "external-unverified@fullworth.local",
                        EmailVerified:
                            false)));

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/register",
                new
                {
                    provider =
                        ExternalIdentityProviders.Apple,

                    idToken =
                        "valid-test-token",

                    password =
                        TestPassword,

                    acceptedTermsAndPrivacy =
                        true,

                    legalTermsVersion =
                        FullWorthLegalDocuments.CurrentVersion
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalRegister_CreatesPasswordBackedLinkedAccount()
    {
        const string email =
            "external-new@fullworth.local";

        const string subject =
            "google-subject-new";

        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                new FixedExternalIdentityTokenValidator(
                    new ExternalIdentity(
                        ExternalIdentityProviders.Google,
                        subject,
                        email,
                        EmailVerified:
                            true)));

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/register",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "valid-test-token",

                    password =
                        TestPassword,

                    acceptedTermsAndPrivacy =
                        true,

                    legalTermsVersion =
                        FullWorthLegalDocuments.CurrentVersion
                });

        response.EnsureSuccessStatusCode();

        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(
            user);

        Assert.True(
            user!.EmailConfirmed);

        Assert.True(
            await userManager.HasPasswordAsync(
                user));

        var logins =
            await userManager.GetLoginsAsync(
                user);

        var login =
            Assert.Single(
                logins);

        Assert.Equal(
            ExternalIdentityProviders.Google,
            login.LoginProvider);

        Assert.Equal(
            subject,
            login.ProviderKey);
    }

    [Fact]
    public async Task ExternalRegister_DoesNotAutoLinkExistingEmailOwner()
    {
        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                new FixedExternalIdentityTokenValidator(
                    new ExternalIdentity(
                        ExternalIdentityProviders.Google,
                        "google-subject-existing",
                        "existing-external@fullworth.local",
                        EmailVerified:
                            true)));

        using var client =
            factory.CreateHttpsClient();

        var existingSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client,
                email:
                    "existing-external@fullworth.local");

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/register",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "valid-test-token",

                    password =
                        TestPassword,

                    acceptedTermsAndPrivacy =
                        true,

                    legalTermsVersion =
                        FullWorthLegalDocuments.CurrentVersion
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var existingUser =
            await userManager.FindByEmailAsync(
                existingSession.Email);

        Assert.NotNull(
            existingUser);

        Assert.Empty(
            await userManager.GetLoginsAsync(
                existingUser!));
    }

    [Fact]
    public async Task ExternalLink_AnonymousCaller_IsRejected()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_AuthenticatedCallerWithoutPasswordReauthentication_IsRejected()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        string.Empty,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_TwoFactorAccountWithoutSecondFactor_IsRejected()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        await using (
            var scope =
                factory.Services.CreateAsyncScope())
        {
            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<
                        UserManager<ApplicationUser>>();

            var user =
                await userManager.FindByEmailAsync(
                    session.Email);

            Assert.NotNull(user);

            var enableResult =
                await userManager.SetTwoFactorEnabledAsync(
                    user!,
                    true);

            Assert.True(
                enableResult.Succeeded);
        }

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task ExternalLink_AuthenticatedCallerWithInvalidProviderToken_FailsWithoutLinking()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var session =
            await TestUserAuthentication
                .RegisterAndLoginAsync(
                    client);

        TestUserAuthentication.Authorize(
            client,
            session);

        using var response =
            await client.PostAsJsonAsync(
                "/api/auth/external/link",
                new
                {
                    provider =
                        ExternalIdentityProviders.Google,

                    idToken =
                        "not-a-real-provider-token",

                    currentPassword =
                        TestPassword,

                    twoFactorCode =
                        (string?)null
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public void ConfigurationBinding_OnlyEnablesProvidersWithAnAudience()
    {
        var settings =
            new Dictionary<string, string?>
            {
                ["ExternalIdentity:Google:Audience"] =
                    "billwatch-google-client-id",

                ["ExternalIdentity:Apple:Audience"] =
                    string.Empty,

                ["ExternalIdentity:Microsoft:Audience"] =
                    string.Empty
            };

        var configuration =
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    settings)
                .Build();

        var validator =
            new ExternalIdentityTokenValidator(
                configuration,
                new TestHttpClientFactory());

        Assert.True(
            validator.IsProviderConfigured(
                ExternalIdentityProviders.Google));

        Assert.False(
            validator.IsProviderConfigured(
                ExternalIdentityProviders.Apple));

        Assert.False(
            validator.IsProviderConfigured(
                ExternalIdentityProviders.Microsoft));

        Assert.False(
            validator.IsProviderConfigured(
                "unknown-provider"));
    }

    private sealed class FixedExternalIdentityTokenValidator(
        ExternalIdentity identity)
        : IExternalIdentityTokenValidator
    {
        public bool IsProviderConfigured(
            string provider)
        {
            return string.Equals(
                provider,
                identity.Provider,
                StringComparison.OrdinalIgnoreCase);
        }

        public Task<ExternalIdentity?> ValidateAsync(
            string provider,
            string idToken,
            CancellationToken cancellationToken = default)
        {
            var matches =
                IsProviderConfigured(
                    provider) &&
                string.Equals(
                    idToken,
                    "valid-test-token",
                    StringComparison.Ordinal);

            return Task.FromResult<ExternalIdentity?>(
                matches
                    ? identity
                    : null);
        }
    }

    private sealed class TestHttpClientFactory :
        IHttpClientFactory
    {
        public HttpClient CreateClient(
            string name)
        {
            return new HttpClient(
                new HttpClientHandler())
            {
                Timeout =
                    TimeSpan.FromSeconds(1)
            };
        }
    }
}

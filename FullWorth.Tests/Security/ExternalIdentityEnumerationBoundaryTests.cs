using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using FullWorth.Core.Legal;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class ExternalIdentityEnumerationBoundaryTests
{
    private const string Password =
        "FullWorth!Tests123";

    private const string Subject =
        "verified-provider-subject";

    private const string Email =
        "external-enumeration@fullworth.local";

    [Fact]
    public async Task ValidUnlinkedIdentity_AndInvalidToken_HaveSamePublicLoginFailure()
    {
        using var factory =
            CreateFactory();

        using var client =
            factory.CreateHttpsClient();

        using var unlinked =
            await LoginAsync(
                client,
                "valid-test-token");

        using var invalid =
            await LoginAsync(
                client,
                "invalid-test-token");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unlinked.StatusCode);

        await AssertSamePublicResponseAsync(
            unlinked,
            invalid);
    }

    [Fact]
    public async Task ExistingEmailAndLinkedIdentity_HaveSamePublicRegistrationFailure()
    {
        using var factory =
            CreateFactory();

        using var client =
            factory.CreateHttpsClient();

        await TestUserAuthentication.RegisterAndLoginAsync(
            client,
            email: Email);

        using var emailOwned =
            await RegisterAsync(
                client);

        await using (
            var scope =
                factory.Services.CreateAsyncScope())
        {
            var manager =
                scope.ServiceProvider.GetRequiredService<
                    UserManager<ApplicationUser>>();

            var owner =
                await manager.FindByEmailAsync(
                    Email);

            Assert.NotNull(
                owner);

            Assert.True(
                (await manager.AddLoginAsync(
                    owner!,
                    new UserLoginInfo(
                        ExternalIdentityProviders.Google,
                        Subject,
                        "Google"))).Succeeded);
        }

        using var identityLinked =
            await RegisterAsync(
                client);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            emailOwned.StatusCode);

        await AssertSamePublicResponseAsync(
            emailOwned,
            identityLinked);

        Assert.DoesNotContain(
            Email,
            await identityLinked.Content.ReadAsStringAsync(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostCreateLoginAssociationFailure_RollsBackAndUsesGenericResponse()
    {
        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                new FixedValidator(),
                services =>
                    services.Replace(
                        ServiceDescriptor.Scoped<
                            UserManager<ApplicationUser>,
                            FailingAddLoginUserManager>()));

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await RegisterAsync(
                client);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "FullWorth could not create this account.",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "LoginAlreadyAssociated",
            body,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            Subject,
            body,
            StringComparison.Ordinal);

        await using var scope =
            factory.Services.CreateAsyncScope();

        var manager =
            scope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        Assert.Null(
            await manager.FindByEmailAsync(
                Email));
    }

    private static FullWorthApiFactory CreateFactory()
    {
        return FullWorthApiFactory.WithExternalIdentityValidator(
            new FixedValidator());
    }

    private static Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string idToken)
    {
        return client.PostAsJsonAsync(
            "/api/auth/external/login",
            new
            {
                provider =
                    ExternalIdentityProviders.Google,
                idToken
            });
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client)
    {
        return client.PostAsJsonAsync(
            "/api/auth/external/register",
            new
            {
                provider =
                    ExternalIdentityProviders.Google,
                idToken =
                    "valid-test-token",
                password =
                    Password,
                acceptedTermsAndPrivacy =
                    true,
                legalTermsVersion =
                    FullWorthLegalDocuments.CurrentVersion
            });
    }

    private static async Task AssertSamePublicResponseAsync(
        HttpResponseMessage first,
        HttpResponseMessage second)
    {
        Assert.Equal(
            first.StatusCode,
            second.StatusCode);

        Assert.Equal(
            first.Content.Headers.ContentType?.ToString(),
            second.Content.Headers.ContentType?.ToString());

        var firstBody =
            JsonNode.Parse(
                await first.Content.ReadAsStringAsync());

        var secondBody =
            JsonNode.Parse(
                await second.Content.ReadAsStringAsync());

        firstBody?.AsObject().Remove(
            "traceId");

        secondBody?.AsObject().Remove(
            "traceId");

        Assert.True(
            JsonNode.DeepEquals(
                firstBody,
                secondBody));

        Assert.False(
            first.Headers.Contains(
                "Set-Cookie"));

        Assert.False(
            second.Headers.Contains(
                "Set-Cookie"));
    }

    private sealed class FixedValidator :
        IExternalIdentityTokenValidator
    {
        public bool IsProviderConfigured(
            string provider)
        {
            return string.Equals(
                provider,
                ExternalIdentityProviders.Google,
                StringComparison.OrdinalIgnoreCase);
        }

        public Task<ExternalIdentity?> ValidateAsync(
            string provider,
            string idToken,
            CancellationToken cancellationToken = default)
        {
            ExternalIdentity? identity =
                IsProviderConfigured(
                    provider) &&
                string.Equals(
                    idToken,
                    "valid-test-token",
                    StringComparison.Ordinal)
                    ? new ExternalIdentity(
                        ExternalIdentityProviders.Google,
                        Subject,
                        Email,
                        EmailVerified: true)
                    : null;

            return Task.FromResult(
                identity);
        }
    }

    private sealed class FailingAddLoginUserManager(
        IUserStore<ApplicationUser> store,
        IOptions<IdentityOptions> options,
        IPasswordHasher<ApplicationUser> passwordHasher,
        IEnumerable<IUserValidator<ApplicationUser>> userValidators,
        IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
        ILookupNormalizer normalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<ApplicationUser>> logger)
        : UserManager<ApplicationUser>(
            store,
            options,
            passwordHasher,
            userValidators,
            passwordValidators,
            normalizer,
            errors,
            services,
            logger)
    {
        public override Task<IdentityResult> AddLoginAsync(
            ApplicationUser user,
            UserLoginInfo login)
        {
            return Task.FromResult(
                IdentityResult.Failed(
                    new IdentityError
                    {
                        Code =
                            "LoginAlreadyAssociated",
                        Description =
                            $"Provider subject {login.ProviderKey} belongs to another account."
                    }));
        }
    }
}

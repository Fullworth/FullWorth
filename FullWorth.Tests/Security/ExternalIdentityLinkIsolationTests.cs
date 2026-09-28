using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class ExternalIdentityLinkIsolationTests
{
    private const string Password =
        "FullWorth!Tests123";

    private const string Subject =
        "owner-provider-subject";

    [Fact]
    public async Task LinkToAnotherUsersProviderIdentity_DoesNotChangeEitherAccount()
    {
        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                new CountingValidator());

        using var client =
            factory.CreateHttpsClient();

        var current =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        var owner =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        string currentStamp;
        string ownerStamp;

        await using (
            var scope =
                factory.Services.CreateAsyncScope())
        {
            var manager =
                scope.ServiceProvider.GetRequiredService<
                    UserManager<ApplicationUser>>();

            var currentUser =
                await manager.FindByEmailAsync(
                    current.Email);

            var ownerUser =
                await manager.FindByEmailAsync(
                    owner.Email);

            Assert.NotNull(
                currentUser);

            Assert.NotNull(
                ownerUser);

            Assert.True(
                (await manager.AddLoginAsync(
                    ownerUser!,
                    new UserLoginInfo(
                        ExternalIdentityProviders.Google,
                        Subject,
                        "Google"))).Succeeded);

            currentStamp =
                await manager.GetSecurityStampAsync(
                    currentUser!);

            ownerStamp =
                await manager.GetSecurityStampAsync(
                    ownerUser);
        }

        TestUserAuthentication.Authorize(
            client,
            current);

        using var response =
            await LinkAsync(
                client,
                Password);

        Assert.Equal(
            HttpStatusCode.Conflict,
            response.StatusCode);

        await using var verificationScope =
            factory.Services.CreateAsyncScope();

        var verificationManager =
            verificationScope.ServiceProvider.GetRequiredService<
                UserManager<ApplicationUser>>();

        var currentAfter =
            await verificationManager.FindByEmailAsync(
                current.Email);

        var ownerAfter =
            await verificationManager.FindByEmailAsync(
                owner.Email);

        Assert.NotNull(
            currentAfter);

        Assert.NotNull(
            ownerAfter);

        Assert.Equal(
            currentStamp,
            await verificationManager.GetSecurityStampAsync(
                currentAfter!));

        Assert.Equal(
            ownerStamp,
            await verificationManager.GetSecurityStampAsync(
                ownerAfter!));

        Assert.Empty(
            await verificationManager.GetLoginsAsync(
                currentAfter));

        var linkedLogin =
            Assert.Single(
                await verificationManager.GetLoginsAsync(
                    ownerAfter));

        Assert.Equal(
            Subject,
            linkedLogin.ProviderKey);
    }

    [Fact]
    public async Task InvalidReauthentication_StopsBeforeProviderTokenValidation()
    {
        var validator =
            new CountingValidator();

        using var factory =
            FullWorthApiFactory.WithExternalIdentityValidator(
                validator);

        using var client =
            factory.CreateHttpsClient();

        var current =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        TestUserAuthentication.Authorize(
            client,
            current);

        using var response =
            await LinkAsync(
                client,
                "wrong-password");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Equal(
            0,
            validator.ValidationCount);
    }

    private static Task<HttpResponseMessage> LinkAsync(
        HttpClient client,
        string currentPassword)
    {
        return client.PostAsJsonAsync(
            "/api/auth/external/link",
            new
            {
                provider =
                    ExternalIdentityProviders.Google,
                idToken =
                    "valid-test-token",
                currentPassword,
                twoFactorCode =
                    (string?)null,
                twoFactorRecoveryCode =
                    (string?)null
            });
    }

    private sealed class CountingValidator :
        IExternalIdentityTokenValidator
    {
        public int ValidationCount { get; private set; }

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
            ValidationCount++;

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
                        "owner-identity@fullworth.local",
                        EmailVerified: true)
                    : null;

            return Task.FromResult(
                identity);
        }
    }
}

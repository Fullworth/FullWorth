using System.Net.Http.Json;
using FullWorth.Core.Legal;
using FullWorth.Core.Security;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class AuthenticationTokenLifetimeTests
{
    [Fact]
    public void BearerOptions_UseExplicitShortLivedSecurityDefaults()
    {
        using var factory =
            new FullWorthApiFactory();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptionsMonitor<
                        BearerTokenOptions>>()
                .Get(
                    IdentityConstants
                        .BearerScheme);

        Assert.Equal(
            AuthenticationSecurityDefaults
                .AccessTokenLifetime,
            options.BearerTokenExpiration);

        Assert.Equal(
            AuthenticationSecurityDefaults
                .RefreshTokenLifetime,
            options.RefreshTokenExpiration);
    }

    [Fact]
    public async Task Login_AdvertisesConfiguredAccessTokenLifetime()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var email =
            $"token-lifetime-{Guid.NewGuid():N}@fullworth.local";

        const string password =
            "FullWorth!TokenTests123";

        using var registerResponse =
            await client.PostAsJsonAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password,
                    acceptedTermsAndPrivacy =
                        true,
                    legalTermsVersion =
                        FullWorthLegalDocuments
                            .CurrentVersion
                });

        registerResponse.EnsureSuccessStatusCode();

        using var loginResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email,
                    password
                });

        loginResponse.EnsureSuccessStatusCode();

        var result =
            await loginResponse.Content
                .ReadFromJsonAsync<
                    TokenResponse>();

        Assert.NotNull(
            result);

        Assert.False(
            string.IsNullOrWhiteSpace(
                result!.AccessToken));

        Assert.False(
            string.IsNullOrWhiteSpace(
                result.RefreshToken));

        Assert.Equal(
            (long)AuthenticationSecurityDefaults
                .AccessTokenLifetime
                .TotalSeconds,
            result.ExpiresIn);
    }

    private sealed record TokenResponse(
        string TokenType,
        string AccessToken,
        long ExpiresIn,
        string RefreshToken);
}

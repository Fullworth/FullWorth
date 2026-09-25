using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class MfaRecoveryEnumerationSecurityTests
{
    private const string Password =
        "FullWorth!Tests123";

    [Fact]
    public async Task PasswordLogin_RecoveryCode_IsRedeemedOnce()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var bootstrapSession =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        string recoveryCode;

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
                    bootstrapSession.Email);

            Assert.NotNull(user);

            var enableResult =
                await userManager.SetTwoFactorEnabledAsync(
                    user!,
                    true);

            Assert.True(enableResult.Succeeded);

            var codes =
                await userManager
                    .GenerateNewTwoFactorRecoveryCodesAsync(
                        user!,
                        1);

            recoveryCode =
                Assert.Single(codes!);

            Assert.Equal(
                1,
                await userManager.CountRecoveryCodesAsync(
                    user!));
        }

        client.DefaultRequestHeaders.Authorization =
            null;

        using var firstResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email =
                        bootstrapSession.Email,

                    password =
                        Password,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        recoveryCode
                });

        Assert.Equal(
            HttpStatusCode.OK,
            firstResponse.StatusCode);

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
                    bootstrapSession.Email);

            Assert.NotNull(user);

            Assert.Equal(
                0,
                await userManager.CountRecoveryCodesAsync(
                    user!));
        }

        using var replayResponse =
            await client.PostAsJsonAsync(
                "/api/auth/login",
                new
                {
                    email =
                        bootstrapSession.Email,

                    password =
                        Password,

                    twoFactorCode =
                        (string?)null,

                    twoFactorRecoveryCode =
                        recoveryCode
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            replayResponse.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_KnownConfirmedAndUnknownEmail_AreIndistinguishable()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var known =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await ConfirmEmailForTestAsync(
            factory,
            known.Email);

        var unknownEmail =
            $"unknown-{Guid.NewGuid():N}@fullworth.local";

        using var knownResponse =
            await client.PostAsJsonAsync(
                "/api/auth/forgotPassword",
                new
                {
                    email =
                        known.Email
                });

        using var unknownResponse =
            await client.PostAsJsonAsync(
                "/api/auth/forgotPassword",
                new
                {
                    email =
                        unknownEmail
                });

        Assert.Equal(
            HttpStatusCode.OK,
            knownResponse.StatusCode);

        Assert.Equal(
            knownResponse.StatusCode,
            unknownResponse.StatusCode);

        Assert.Equal(
            await knownResponse.Content.ReadAsStringAsync(),
            await unknownResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ResetPassword_InvalidTokenForKnownConfirmedAndUnknownEmail_ReturnsSamePublicError()
    {
        await using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateHttpsClient();

        var known =
            await TestUserAuthentication.RegisterAndLoginAsync(
                client);

        await ConfirmEmailForTestAsync(
            factory,
            known.Email);

        var unknownEmail =
            $"unknown-{Guid.NewGuid():N}@fullworth.local";

        var requestCode =
            "not-a-valid-reset-code";

        using var knownResponse =
            await client.PostAsJsonAsync(
                "/api/auth/resetPassword",
                new
                {
                    email =
                        known.Email,

                    resetCode =
                        requestCode,

                    newPassword =
                        "FullWorth!Replacement123"
                });

        using var unknownResponse =
            await client.PostAsJsonAsync(
                "/api/auth/resetPassword",
                new
                {
                    email =
                        unknownEmail,

                    resetCode =
                        requestCode,

                    newPassword =
                        "FullWorth!Replacement123"
                });

        Assert.Equal(
            HttpStatusCode.BadRequest,
            knownResponse.StatusCode);

        Assert.Equal(
            knownResponse.StatusCode,
            unknownResponse.StatusCode);

        Assert.Equal(
            await ReadErrorFingerprintAsync(
                knownResponse),
            await ReadErrorFingerprintAsync(
                unknownResponse));
    }

    private static async Task ConfirmEmailForTestAsync(
        FullWorthApiFactory factory,
        string email)
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var userManager =
            scope.ServiceProvider
                .GetRequiredService<
                    UserManager<ApplicationUser>>();

        var user =
            await userManager.FindByEmailAsync(
                email);

        Assert.NotNull(user);

        user!.EmailConfirmed =
            true;

        var updateResult =
            await userManager.UpdateAsync(
                user);

        Assert.True(updateResult.Succeeded);
    }

    private static async Task<string>
        ReadErrorFingerprintAsync(
            HttpResponseMessage response)
    {
        using var document =
            JsonDocument.Parse(
                await response.Content
                    .ReadAsStringAsync());

        var root =
            document.RootElement;

        Assert.True(
            root.TryGetProperty(
                "errors",
                out var errors));

        var properties =
            errors.EnumerateObject()
                .OrderBy(
                    property =>
                        property.Name,
                    StringComparer.Ordinal)
                .Select(
                    property =>
                        new
                        {
                            property.Name,
                            Values =
                                property.Value
                                    .EnumerateArray()
                                    .Select(
                                        value =>
                                            value.GetString() ??
                                            string.Empty)
                                    .ToArray()
                        })
                .ToArray();

        return JsonSerializer.Serialize(
            properties);
    }
}

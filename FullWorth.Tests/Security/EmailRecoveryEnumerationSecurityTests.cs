using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FullWorth.API.Data.Entities;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class EmailRecoveryEnumerationSecurityTests
{
    [Theory]
    [InlineData("/api/auth/resendConfirmationEmail", false)]
    [InlineData("/api/auth/resendConfirmationEmail", true)]
    [InlineData("/api/auth/forgotPassword", false)]
    [InlineData("/api/auth/forgotPassword", true)]
    public async Task EmailRequest_KnownAndUnknownAccount_ReturnSamePublicResponse(
        string path,
        bool emailConfirmed)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var email = await CreateAccountAsync(factory, emailConfirmed);

        // These requests deliberately have no authentication header.
        using var known = await client.PostAsJsonAsync(path, new { email });
        using var unknown = await client.PostAsJsonAsync(
            path,
            new { email = $"unknown-{Guid.NewGuid():N}@fullworth.local" });

        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        await AssertSamePublicResponseAsync(known, unknown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResetPassword_InvalidProof_KnownAndUnknownAccount_ReturnSamePublicError(
        bool emailConfirmed)
    {
        await using var factory = new FullWorthApiFactory();
        using var client = factory.CreateHttpsClient();
        var email = await CreateAccountAsync(factory, emailConfirmed);

        // Valid base64url encoding, but not a protected Identity reset token.
        const string resetCode = "bm90LWEtdmFsaWQtdG9rZW4";
        const string newPassword = "FullWorth!Replacement123";

        using var known = await client.PostAsJsonAsync(
            "/api/auth/resetPassword",
            new { email, resetCode, newPassword });
        using var unknown = await client.PostAsJsonAsync(
            "/api/auth/resetPassword",
            new
            {
                email = $"unknown-{Guid.NewGuid():N}@fullworth.local",
                resetCode,
                newPassword
            });

        Assert.Equal(HttpStatusCode.BadRequest, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(
            known.Content.Headers.ContentType?.ToString(),
            unknown.Content.Headers.ContentType?.ToString());
        Assert.Equal(await ReadErrorsAsync(known), await ReadErrorsAsync(unknown));

        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await manager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.True(await manager.CheckPasswordAsync(user!, "FullWorth!Tests123"));
        Assert.False(await manager.CheckPasswordAsync(user!, newPassword));
    }

    private static async Task<string> CreateAccountAsync(
        FullWorthApiFactory factory,
        bool emailConfirmed)
    {
        // Arrange through Identity directly so registration/login response behavior
        // cannot mask an anonymous email/recovery endpoint regression.
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"email-recovery-{Guid.NewGuid():N}@fullworth.local";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            EmailConfirmed = emailConfirmed
        };
        var result = await manager.CreateAsync(user, "FullWorth!Tests123");
        Assert.True(result.Succeeded);
        return email;
    }

    private static async Task AssertSamePublicResponseAsync(
        HttpResponseMessage known,
        HttpResponseMessage unknown)
    {
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(
            known.Content.Headers.ContentType?.ToString(),
            unknown.Content.Headers.ContentType?.ToString());
        Assert.Equal(known.Headers.Location, unknown.Headers.Location);
        Assert.Equal(
            await known.Content.ReadAsStringAsync(),
            await unknown.Content.ReadAsStringAsync());
    }

    private static async Task<string> ReadErrorsAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = document.RootElement.GetProperty("errors");
        return JsonSerializer.Serialize(
            errors.EnumerateObject()
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .Select(property => new
                {
                    property.Name,
                    Values = property.Value.EnumerateArray()
                        .Select(value => value.GetString()).ToArray()
                }).ToArray());
    }
}

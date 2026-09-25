using System.Net;
using System.Net.Http.Json;
using FullWorth.API.Data.Entities;
using FullWorth.API.Services.Identity;
using FullWorth.Core.Legal;
using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class IdentityEmailFailureSecurityTests
{
    [Theory]
    [InlineData("/api/auth/forgotPassword", 0)]
    [InlineData("/api/auth/forgotPassword", 1)]
    [InlineData("/api/auth/forgotPassword", 2)]
    [InlineData("/api/auth/resendConfirmationEmail", 0)]
    [InlineData("/api/auth/resendConfirmationEmail", 1)]
    [InlineData("/api/auth/resendConfirmationEmail", 2)]
    [InlineData("/api/auth/register", 0)]
    [InlineData("/api/auth/register", 1)]
    [InlineData("/api/auth/register", 2)]
    public async Task DeliveryFailure_DoesNotDiscloseAccountExistence(string path, int failureMode)
    {
        using var handler = new FailingProviderHandler(failureMode);
        using var providerClient = new HttpClient(handler) { BaseAddress = new Uri("https://provider.invalid/") };
        var sender = new ResendIdentityEmailSender(providerClient, Options.Create(new IdentityEmailOptions
        {
            Enabled = true,
            ApiKey = "test-key",
            FromAddress = "security@fullworth.local",
            FromName = "FullWorth",
            PublicWebBaseUrl = "https://fullworth.local"
        }));
        await using var root = new FullWorthApiFactory();
        await using var factory = root.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender<ApplicationUser>>();
            services.AddSingleton<IEmailSender<ApplicationUser>>(sender);
        }));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        var email = $"known-{Guid.NewGuid():N}@fullworth.local";
        string? stamp;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { Id = Guid.NewGuid(), Email = email, UserName = email, EmailConfirmed = true };
            Assert.True((await manager.CreateAsync(user, "FullWorth!Tests123")).Succeeded);
            stamp = user.SecurityStamp;
        }

        object Request(string address) => new
        {
            email = address,
            password = "FullWorth!Different456",
            acceptedTermsAndPrivacy = true,
            legalTermsVersion = FullWorthLegalDocuments.CurrentVersion
        };
        using var known = await client.PostAsJsonAsync(path, Request(email));
        using var unknown = await client.PostAsJsonAsync(path, Request($"unknown-{Guid.NewGuid():N}@fullworth.local"));
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Content.Headers.ContentType?.ToString(), unknown.Content.Headers.ContentType?.ToString());
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.False(known.Headers.Contains("Set-Cookie"));
        Assert.False(unknown.Headers.Contains("Set-Cookie"));
        Assert.Equal(1, handler.Calls);

        await using var verification = factory.Services.CreateAsyncScope();
        var users = verification.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByEmailAsync(email);
        Assert.NotNull(existing);
        Assert.Equal(stamp, existing!.SecurityStamp);
        Assert.True(await users.CheckPasswordAsync(existing, "FullWorth!Tests123"));
    }

    [Fact]
    public async Task Sender_DoesNotRetainTransportExceptionDetails()
    {
        using var handler = new FailingProviderHandler(1);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://provider.invalid/") };
        var sender = new ResendIdentityEmailSender(client, Options.Create(new IdentityEmailOptions
        {
            Enabled = true, ApiKey = "test-key", FromAddress = "security@fullworth.local",
            FromName = "FullWorth", PublicWebBaseUrl = "https://fullworth.local"
        }));
        var exception = await Assert.ThrowsAsync<IdentityEmailDeliveryException>(() =>
            sender.SendPasswordResetCodeAsync(new ApplicationUser(), "user@fullworth.local", "secret-test-code"));
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("sensitive-provider-detail", exception.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Filter_DoesNotHideUnrelatedOperationsOrAbortedRequests(bool aborted)
    {
        var filter = new AnonymousIdentityEmailDeliveryEndpointFilter(
            NullLogger<AnonymousIdentityEmailDeliveryEndpointFilter>.Instance);
        var http = new DefaultHttpContext();
        http.RequestAborted = new CancellationToken(aborted);
        object request = aborted ? new ForgotPasswordRequest { Email = "user@fullworth.local" }
            : new ResetPasswordRequest { Email = "user@fullworth.local", ResetCode = "invalid", NewPassword = "invalid" };
        var context = new DefaultEndpointFilterInvocationContext(http, request);
        await Assert.ThrowsAsync<IdentityEmailDeliveryException>(async () =>
            await filter.InvokeAsync(context, _ => throw new IdentityEmailDeliveryException()));
    }

    [Fact]
    public async Task Filter_DoesNotHideConfigurationOrApplicationFailures()
    {
        var filter = new AnonymousIdentityEmailDeliveryEndpointFilter(
            NullLogger<AnonymousIdentityEmailDeliveryEndpointFilter>.Instance);
        var context = new DefaultEndpointFilterInvocationContext(new DefaultHttpContext(),
            new ForgotPasswordRequest { Email = "user@fullworth.local" });
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await filter.InvokeAsync(context, _ => throw new InvalidOperationException("configuration failure")));
    }

    private sealed class FailingProviderHandler(int mode) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return mode switch
            {
                1 => Task.FromException<HttpResponseMessage>(new HttpRequestException("sensitive-provider-detail")),
                2 => Task.FromException<HttpResponseMessage>(new TaskCanceledException("sensitive-provider-detail")),
                _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("sensitive-provider-detail")
                })
            };
        }
    }
}

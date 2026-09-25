using System.Net;
using FullWorth.Web.Services;
using Microsoft.AspNetCore.Http;

namespace FullWorth.Tests.Security;

public sealed class RegistrationEnumerationSecurityTests
{
    private const string Email =
        "registration-enumeration@fullworth.local";

    private const string Password =
        "FullWorth!Registration123";

    [Fact]
    public async Task NewAndDuplicateEmailRegistration_ReturnSamePublicOutcome()
    {
        var newRegistration =
            await ExecuteRegistrationAsync(
                new HttpResponseMessage(
                    HttpStatusCode.OK));

        var duplicateRegistration =
            await ExecuteRegistrationAsync(
                JsonResponse(
                    HttpStatusCode.BadRequest,
                    """
                    {
                      "errors": {
                        "DuplicateEmail": [
                          "Email 'registration-enumeration@fullworth.local' is already taken."
                        ]
                      }
                    }
                    """));

        Assert.Equal(
            AuthOperationResult.Success,
            newRegistration.Result);

        Assert.Equal(
            AuthOperationResult.Success,
            duplicateRegistration.Result);

        Assert.Equal(
            newRegistration.Result,
            duplicateRegistration.Result);

        Assert.Equal(
            ["/api/auth/register"],
            newRegistration.Paths);

        Assert.Equal(
            ["/api/auth/register"],
            duplicateRegistration.Paths);
    }

    [Fact]
    public async Task PasswordValidationFailure_RemainsVisibleWithoutLeakingIdentityState()
    {
        var registration =
            await ExecuteRegistrationAsync(
                JsonResponse(
                    HttpStatusCode.BadRequest,
                    """
                    {
                      "errors": {
                        "PasswordTooShort": [
                          "Passwords must be at least 12 characters."
                        ]
                      }
                    }
                    """));

        Assert.False(
            registration.Result.Succeeded);

        Assert.Equal(
            "Passwords must be at least 12 characters.",
            registration.Result.ErrorMessage);

        Assert.Equal(
            ["/api/auth/register"],
            registration.Paths);
    }

    private static async Task<RegistrationAttempt>
        ExecuteRegistrationAsync(
            HttpResponseMessage response)
    {
        using var handler =
            new CapturingHandler(
                response);

        using var factory =
            new SingleClientFactory(
                handler);

        var service =
            new WebAuthenticationService(
                factory);

        var context =
            new DefaultHttpContext();

        var result =
            await service.RegisterAsync(
                context,
                Email,
                Password,
                acceptedTermsAndPrivacy:
                    true,
                legalTermsVersion:
                    "test");

        return new RegistrationAttempt(
            result,
            handler.Paths);
    }

    private static HttpResponseMessage
        JsonResponse(
            HttpStatusCode statusCode,
            string json)
    {
        return new HttpResponseMessage(
            statusCode)
        {
            Content =
                new StringContent(
                    json,
                    System.Text.Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class CapturingHandler(
        params HttpResponseMessage[] responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage>
            _responses =
                new(
                    responses);

        public List<string> Paths { get; } =
            [];

        protected override Task<HttpResponseMessage>
            SendAsync(
                HttpRequestMessage request,
                CancellationToken cancellationToken)
        {
            Paths.Add(
                request.RequestUri?.AbsolutePath
                ?? throw new InvalidOperationException(
                    "Request URI was missing."));

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    "No scripted response remains.");
            }

            return Task.FromResult(
                _responses.Dequeue());
        }
    }

    private sealed class SingleClientFactory(
        HttpMessageHandler handler)
        : IHttpClientFactory,
          IDisposable
    {
        private readonly HttpClient _client =
            new(
                handler,
                disposeHandler:
                    false)
            {
                BaseAddress =
                    new Uri(
                        "https://api.invalid")
            };

        public HttpClient CreateClient(
            string name)
        {
            Assert.Equal(
                "FullWorthApi",
                name);

            return _client;
        }

        public void Dispose()
        {
            _client.Dispose();
        }
    }

    private sealed record RegistrationAttempt(
        AuthOperationResult Result,
        IReadOnlyList<string> Paths);
}

using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.API.Services.Plaid;

/// <summary>
/// Protects outbound provider credentials from transparent HTTP redirects.
/// HttpClientHandler follows redirects by default; only this hardened client
/// may carry Plaid's custom authentication headers in production.
/// </summary>
public static class PlaidHttpClientRegistration
{
    public static IHttpClientBuilder AddPlaidApiClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddHttpClient<PlaidApiClient>()
            .ConfigurePrimaryHttpMessageHandler(CreatePrimaryHandler);
    }

    internal static HttpMessageHandler CreatePrimaryHandler()
    {
        return new SocketsHttpHandler
        {
            // Custom PLAID-CLIENT-ID and PLAID-SECRET headers are not
            // automatically stripped like Authorization on a redirect.
            // A response with Location must never trigger another request.
            AllowAutoRedirect = false,

            // Provider requests should not acquire or replay cookies.
            UseCookies = false
        };
    }
}

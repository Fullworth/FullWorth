using Microsoft.AspNetCore.Authentication.Cookies;
using StackExchange.Redis;

namespace FullWorth.Web.Infrastructure;

public static class WebSessionStoreExtensions
{
    public static IServiceCollection
        AddFullWorthWebSessionStore(
            this IServiceCollection services,
            IConfiguration configuration,
            IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var redisHost =
            configuration[
                "WebSession:RedisHost"]?
                .Trim();

        var redisPassword =
            configuration[
                "WebSession:RedisPassword"];

        if (environment.IsDevelopment() &&
            (string.IsNullOrWhiteSpace(redisHost) ||
             string.IsNullOrWhiteSpace(redisPassword)))
        {
            services.AddDistributedMemoryCache();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(redisHost) ||
                Uri.CheckHostName(redisHost) ==
                    UriHostNameType.Unknown)
            {
                throw new InvalidOperationException(
                    "WebSession:RedisHost must be a valid host outside development.");
            }

            if (string.IsNullOrWhiteSpace(redisPassword) ||
                redisPassword.Length < 32)
            {
                throw new InvalidOperationException(
                    "WebSession:RedisPassword must contain at least 32 characters outside development.");
            }

            services.AddStackExchangeRedisCache(
                options =>
                {
                    var redisConfiguration =
                        new ConfigurationOptions
                        {
                            AbortOnConnectFail =
                                false,

                            ClientName =
                                "fullworth-web-session",

                            Password =
                                redisPassword,

                            Ssl =
                                false
                        };

                    redisConfiguration.EndPoints.Add(
                        redisHost,
                        6379);

                    options.ConfigurationOptions =
                        redisConfiguration;

                    options.InstanceName =
                        "fullworth:web-session:";
                });
        }

        services.AddSingleton(
            TimeProvider.System);

        services.AddSingleton<
            ProtectedDistributedTicketStore>();

        services.AddOptions<
                CookieAuthenticationOptions>(
                CookieAuthenticationDefaults
                    .AuthenticationScheme)
            .Configure<
                ProtectedDistributedTicketStore>(
                (
                    options,
                    store) =>
                {
                    options.SessionStore =
                        store;
                });

        return services;
    }
}

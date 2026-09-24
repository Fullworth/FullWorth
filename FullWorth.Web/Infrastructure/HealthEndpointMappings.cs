using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace FullWorth.Web.Infrastructure;

public static class HealthEndpointMappings
{
    public static IEndpointRouteBuilder
        MapFullWorthHealthEndpoints(
            this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(
            endpoints);

        endpoints.MapGet(
                "/health/live",
                () =>
                    Results.Ok(
                        new
                        {
                            status =
                                "live"
                        }))
            .AllowAnonymous();

        endpoints.MapGet(
                "/health/ready",
                async (
                    IHttpClientFactory httpClientFactory,
                    IDistributedCache sessionCache,
                    CancellationToken cancellationToken) =>
                {
                    try
                    {
                        using var timeout =
                            CancellationTokenSource
                                .CreateLinkedTokenSource(
                                    cancellationToken);

                        timeout.CancelAfter(
                            TimeSpan.FromSeconds(5));

                        var sessionProbeKey =
                            "health:" +
                            Guid.NewGuid()
                                .ToString("N");

                        var sessionProbeValue =
                            Guid.NewGuid()
                                .ToByteArray();

                        await sessionCache.SetAsync(
                            sessionProbeKey,
                            sessionProbeValue,
                            new DistributedCacheEntryOptions
                            {
                                AbsoluteExpirationRelativeToNow =
                                    TimeSpan.FromSeconds(30)
                            },
                            timeout.Token);

                        var sessionProbeRead =
                            await sessionCache.GetAsync(
                                sessionProbeKey,
                                timeout.Token);

                        await sessionCache.RemoveAsync(
                            sessionProbeKey,
                            timeout.Token);

                        if (sessionProbeRead is null ||
                            !sessionProbeRead.SequenceEqual(
                                sessionProbeValue))
                        {
                            return Results.StatusCode(
                                StatusCodes
                                    .Status503ServiceUnavailable);
                        }

                        var client =
                            httpClientFactory
                                .CreateClient(
                                    "FullWorthApi");

                        using var response =
                            await client.GetAsync(
                                "/health/ready",
                                HttpCompletionOption
                                    .ResponseHeadersRead,
                                timeout.Token);

                        if (!response.IsSuccessStatusCode)
                        {
                            return Results.StatusCode(
                                StatusCodes
                                    .Status503ServiceUnavailable);
                        }

                        var body =
                            await response.Content
                                .ReadAsStringAsync(
                                    timeout.Token);

                        var normalized =
                            string.Concat(
                                body.Where(
                                    character =>
                                        !char.IsWhiteSpace(
                                            character)));

                        if (!string.Equals(
                                normalized,
                                "{\"status\":\"ready\"}",
                                StringComparison.Ordinal))
                        {
                            return Results.StatusCode(
                                StatusCodes
                                    .Status503ServiceUnavailable);
                        }

                        return Results.Ok(
                            new
                            {
                                status =
                                    "ready"
                            });
                    }
                    catch (OperationCanceledException)
                    {
                        return Results.StatusCode(
                            StatusCodes
                                .Status503ServiceUnavailable);
                    }
                    catch (HttpRequestException)
                    {
                        return Results.StatusCode(
                            StatusCodes
                                .Status503ServiceUnavailable);
                    }
                    catch (InvalidOperationException)
                    {
                        return Results.StatusCode(
                            StatusCodes
                                .Status503ServiceUnavailable);
                    }
                    catch (RedisException)
                    {
                        return Results.StatusCode(
                            StatusCodes
                                .Status503ServiceUnavailable);
                    }
                })
            .AllowAnonymous();

        return endpoints;
    }
}
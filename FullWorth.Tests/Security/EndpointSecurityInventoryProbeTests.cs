using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class EndpointSecurityInventoryProbeTests
{
    [Fact]
    public void PrintApplicationEndpointSecurityMetadata()
    {
        using var apiFactory =
            new FullWorthApiFactory();

        using var apiClient =
            apiFactory.CreateHttpsClient();

        using var webFactory =
            new FullWorthWebFactory();

        using var webClient =
            webFactory.CreateHttpsClient();

        var rows =
            GetRows(
                    "API",
                    apiFactory.Services)
                .Concat(
                    GetRows(
                        "WEB",
                        webFactory.Services))
                .OrderBy(
                    row =>
                        row,
                    StringComparer.Ordinal)
                .ToArray();

        Assert.True(
            false,
            "ENDPOINT SECURITY INVENTORY\n" +
            string.Join(
                "\n",
                rows));
    }

    private static IEnumerable<string> GetRows(
        string host,
        IServiceProvider services)
    {
        var endpointDataSource =
            services.GetRequiredService<EndpointDataSource>();

        foreach (var endpoint in
                 endpointDataSource.Endpoints
                     .OfType<RouteEndpoint>())
        {
            var httpMethods =
                endpoint.Metadata
                    .GetMetadata<HttpMethodMetadata>()?
                    .HttpMethods;

            if (httpMethods is null ||
                httpMethods.Count ==
                    0)
            {
                continue;
            }

            var route =
                endpoint.RoutePattern.RawText
                ?? "<unknown>";

            var authentication =
                endpoint.Metadata
                    .GetMetadata<IAllowAnonymous>() is not null
                    ? "anonymous-explicit"
                    : endpoint.Metadata
                        .GetOrderedMetadata<IAuthorizeData>()
                        .Count >
                      0
                        ? "authenticated"
                        : "anonymous-implicit";

            var namedRateLimit =
                endpoint.Metadata
                    .GetOrderedMetadata<
                        EnableRateLimitingAttribute>()
                    .LastOrDefault()?
                    .PolicyName;

            var rateLimit =
                endpoint.Metadata
                    .GetMetadata<
                        DisableRateLimitingAttribute>() is not null
                    ? "disabled"
                    : namedRateLimit is null
                        ? "global"
                        : $"named:{namedRateLimit}";

            foreach (var method in
                     httpMethods.OrderBy(
                         value =>
                             value,
                         StringComparer.Ordinal))
            {
                yield return
                    $"{host}|{method}|/{route.TrimStart('/')}|{authentication}|{rateLimit}";
            }
        }
    }
}

using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace FullWorth.Tests.Security;

public sealed class WebBffStatementUploadBodyLimitTests
{
    [Fact]
    public void StatementUploadEndpoint_EnforcesRequestAndMultipartBodyLimits()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        var endpointDataSource =
            factory.Services.GetRequiredService<EndpointDataSource>();

        var endpoint =
            Assert.Single(
                endpointDataSource.Endpoints
                    .OfType<RouteEndpoint>()
                    .Where(candidate =>
                        candidate.RoutePattern.RawText?
                            .Contains(
                                "/bill-streams/{billStreamId:guid}/statement-uploads",
                                StringComparison.Ordinal) ==
                            true &&
                        candidate.Metadata
                            .GetMetadata<HttpMethodMetadata>()?
                            .HttpMethods
                            .Contains(
                                HttpMethods.Post,
                                StringComparer.OrdinalIgnoreCase) ==
                            true));

        const long expectedLimit =
            16L * 1024 * 1024;

        Assert.Equal(
            expectedLimit,
            endpoint.Metadata
                .GetMetadata<IRequestSizeLimitMetadata>()?
                .MaxRequestBodySize);

        Assert.Equal(
            expectedLimit,
            endpoint.Metadata
                .GetMetadata<IFormOptionsMetadata>()?
                .MultipartBodyLengthLimit);
    }
}

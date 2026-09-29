using FullWorth.Tests.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FullWorth.Tests.Security;

public sealed class RequestBodyLimitConfigurationTests
{
    private const long ExpectedDefaultRequestBodyLimit =
        1L * 1024 * 1024;

    [Fact]
    public void ApiHost_UsesOneMegabyteDefaultRequestBodyLimit()
    {
        using var factory =
            new FullWorthApiFactory();

        using var client =
            factory.CreateClient();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptions<KestrelServerOptions>>()
                .Value;

        Assert.Equal(
            ExpectedDefaultRequestBodyLimit,
            options.Limits.MaxRequestBodySize);

        Assert.Equal(
            8 * 1024,
            options.Limits.MaxRequestLineSize);

        Assert.Equal(
            32 * 1024,
            options.Limits.MaxRequestHeadersTotalSize);
    }

    [Fact]
    public void WebHost_UsesOneMegabyteDefaultRequestBodyLimit()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        var options =
            factory.Services
                .GetRequiredService<
                    IOptions<KestrelServerOptions>>()
                .Value;

        Assert.Equal(
            ExpectedDefaultRequestBodyLimit,
            options.Limits.MaxRequestBodySize);
    }
}

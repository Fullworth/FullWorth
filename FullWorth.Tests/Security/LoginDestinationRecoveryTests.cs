using System.Net;
using System.Text.RegularExpressions;
using FullWorth.Tests.Infrastructure;
using FullWorth.Web.Services;

namespace FullWorth.Tests.Security;

public sealed class LoginDestinationRecoveryTests
{
    [Theory]
    [InlineData("/app/account/settings")]
    [InlineData("/app/bills/10000000-0000-0000-0000-000000000001?tab=evidence")]
    [InlineData("/app/planning#history")]
    public void LocalDestinationsArePreserved(string value) => Assert.Equal(value, LocalLoginDestination.Normalize(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.test/")]
    [InlineData("//example.test/")]
    [InlineData("/\\example.test/")]
    [InlineData("/%2fexample.test")]
    [InlineData("/%255cexample.test")]
    [InlineData("/app%0d%0aLocation:evil")]
    public void InvalidDestinationsUseSafeFallback(string? value) => Assert.Equal("/app", LocalLoginDestination.Normalize(value));

    [Theory]
    [InlineData('&')]
    [InlineData('é')]
    [InlineData('x')]
    public void OversizedSerializedDestinationCannotOverflowPendingMfaCookie(char character)
    {
        var destination = "/app?state=" + new string(character, 600);
        Assert.Equal("/app", LocalLoginDestination.Normalize(destination));
    }

    [Fact]
    public async Task LoginRendersOnlyNormalizedDestinationAndNeverTheHostileOriginal()
    {
        await using var web = new FullWorthWebFactory();
        using var client = web.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        var html = await client.GetStringAsync("/login?ReturnUrl=" + Uri.EscapeDataString("//evil.example.test"));
        Assert.Matches(@"name=""returnUrl""[^>]*value=""/app""", html);
        Assert.DoesNotContain("evil.example.test", html);
        html = await client.GetStringAsync("/login?ReturnUrl=" + Uri.EscapeDataString("/app/account/settings"));
        Assert.Matches(@"name=""returnUrl""[^>]*value=""/app/account/settings""", html);
    }
}

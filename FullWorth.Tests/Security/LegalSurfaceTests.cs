using System.Net;
using FullWorth.Core.Legal;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class LegalSurfaceTests
{
    [Fact]
    public async Task RegistrationPage_RendersRequiredVersionedLegalConsent()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(
                "/register");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            "name=\"acceptedTermsAndPrivacy\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "required",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "name=\"legalTermsVersion\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            FullWorthLegalDocuments.CurrentVersion,
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "href=\"/terms\"",
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "href=\"/privacy\"",
            body,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/terms", "Terms of Service")]
    [InlineData("/privacy", "Privacy Notice")]
    public async Task LegalDocuments_ArePubliclyReadable(
        string route,
        string expectedTitle)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var response =
            await client.GetAsync(route);

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);

        var body =
            await response.Content.ReadAsStringAsync();

        Assert.Contains(
            expectedTitle,
            body,
            StringComparison.Ordinal);

        Assert.Contains(
            "September 4, 2026",
            body,
            StringComparison.Ordinal);
    }
}

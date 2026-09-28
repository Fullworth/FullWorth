using System.Net;
using System.Text;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebPlanningBffBoundaryTests
{
    [Theory]
    [InlineData("/bff/planning/pay-schedule")]
    [InlineData("/bff/planning/bill-funding-preferences")]
    [InlineData("/bff/planning/upcoming-bill-changes")]
    public async Task PlanningReads_AnonymousSession_IsRejected(
        string route)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Test-Anonymous",
            "true");

        using var response =
            await client.GetAsync(
                route);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Theory]
    [InlineData(
        "PUT",
        "/bff/planning/pay-schedule")]
    [InlineData(
        "PUT",
        "/bff/planning/bill-funding-preferences/11111111-1111-1111-1111-111111111111")]
    [InlineData(
        "DELETE",
        "/bff/planning/bill-funding-preferences/11111111-1111-1111-1111-111111111111")]
    public async Task PlanningWrites_MissingAntiforgeryToken_AreRejected(
        string method,
        string route)
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                new HttpMethod(
                    method),
                route);

        if (!string.Equals(
                method,
                "DELETE",
                StringComparison.Ordinal))
        {
            request.Content =
                new StringContent(
                    "{}",
                    Encoding.UTF8,
                    "application/json");
        }

        using var response =
            await client.SendAsync(
                request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
    }

    [Fact]
    public async Task EmptyBillStreamId_IsNotRoutedToApi()
    {
        using var factory =
            new FullWorthWebFactory();

        using var client =
            factory.CreateHttpsClient();

        using var request =
            new HttpRequestMessage(
                HttpMethod.Delete,
                "/bff/planning/bill-funding-preferences/00000000-0000-0000-0000-000000000000");

        using var response =
            await client.SendAsync(
                request);

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);
    }
}

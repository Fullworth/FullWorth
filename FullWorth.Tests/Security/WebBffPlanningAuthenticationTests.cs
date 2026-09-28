using System.Net;
using System.Net.Http.Json;
using FullWorth.Tests.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class WebBffPlanningAuthenticationTests
{
    [Fact]
    public async Task GetPaySchedule_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Test-Anonymous",
            "true");

        using var response =
            await client.GetAsync(
                "/bff/planning/pay-schedule");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }

    [Fact]
    public async Task PutPaySchedule_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory();
        using var client = factory.CreateHttpsClient();
        client.DefaultRequestHeaders.Add(
            "X-FullWorth-Test-Anonymous",
            "true");

        using var response =
            await client.PutAsJsonAsync(
                "/bff/planning/pay-schedule",
                new
                {
                    frequency = "Biweekly",
                    anchorPayDate = "2026-09-28",
                    secondaryDayOfMonth = (int?)null,
                    defaultPaychecksAhead = 1
                });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
    }
}

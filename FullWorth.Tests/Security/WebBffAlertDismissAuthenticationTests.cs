using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffAlertDismissAuthenticationTests
{
    [Fact] public async Task Dismiss_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsync($"/bff/alerts/{Guid.NewGuid()}/dismiss", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

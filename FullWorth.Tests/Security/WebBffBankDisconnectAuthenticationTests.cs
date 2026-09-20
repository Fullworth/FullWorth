using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffBankDisconnectAuthenticationTests
{
    [Fact] public async Task Disconnect_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.DeleteAsync($"/bff/bank-connections/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

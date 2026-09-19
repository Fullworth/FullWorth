using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffStatementDownloadAuthenticationTests
{
    [Fact] public async Task Download_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.GetAsync($"/bff/bill-streams/{Guid.NewGuid()}/statement-uploads/{Guid.NewGuid()}/file");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffStatementUploadAuthenticationTests
{
    [Fact] public async Task Upload_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.PostAsync($"/bff/bill-streams/{Guid.NewGuid()}/statement-uploads", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

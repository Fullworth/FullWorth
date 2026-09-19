using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class WebBffAccountDeleteAuthenticationTests
{
    [Fact] public async Task DeleteAccount_AnonymousSession_IsRejected()
    {
        using var factory = new FullWorthWebFactory(); using var client = factory.CreateHttpsClient(); client.DefaultRequestHeaders.Add("X-FullWorth-Test-Anonymous", "true");
        using var response = await client.DeleteAsync("/bff/account");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

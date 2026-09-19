using System.Net;
using System.Net.Http.Json;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class TwoFactorDisableAuthorizationTests
{
    [Fact] public async Task Disable_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/account/security/two-factor/disable", new { currentPassword = "x", twoFactorCode = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

using System.Net;
using System.Net.Http.Json;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class RecoveryCodeAuthorizationTests
{
    [Fact] public async Task Regenerate_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsJsonAsync("/api/account/security/two-factor/recovery-codes", new { currentPassword = "x", twoFactorCode = "000000" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

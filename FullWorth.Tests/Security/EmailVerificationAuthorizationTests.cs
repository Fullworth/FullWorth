using System.Net;
using FullWorth.Tests.Infrastructure;
namespace FullWorth.Tests.Security;
public sealed class EmailVerificationAuthorizationTests
{
    [Fact] public async Task ResendVerification_AnonymousUser_IsRejected()
    {
        await using var factory = new FullWorthApiFactory(); using var client = factory.CreateHttpsClient();
        using var response = await client.PostAsync("/api/account/security/email/verification", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace FullWorth.Web.Services;

public sealed record PendingWebPasswordLogin(
    string LoginId, DateTimeOffset ExpiresAt, string Email, bool RememberMe, bool CanUsePasskey, string ReturnUrl = "/app");

// Opaque, short-lived password-proof cookie, never a normal authenticated session.
// No password, access token, refresh token or MFA secret is carried here.
public sealed class PendingPasswordLoginCookie(IDataProtectionProvider protection, TimeProvider clock)
{
    public const string Name = "__Host-FullWorth.PendingPassword";
    private readonly ITimeLimitedDataProtector protector = protection
        .CreateProtector("FullWorth.PendingPassword.v1").ToTimeLimitedDataProtector();

    public void Write(HttpContext context, PendingWebPasswordLogin login)
    {
        context.Response.Cookies.Append(Name,
            protector.Protect(JsonSerializer.Serialize(login), login.ExpiresAt),
            new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
                Path = "/", IsEssential = true, Expires = login.ExpiresAt });
    }

    public PendingWebPasswordLogin? Read(HttpContext context)
    {
        if (!context.Request.Cookies.TryGetValue(Name, out var value) || value.Length > 4096) return null;
        try
        {
            var login = JsonSerializer.Deserialize<PendingWebPasswordLogin>(protector.Unprotect(value));
            return login is not null && login.LoginId?.Length == 43 && login.ExpiresAt > clock.GetUtcNow() ? login : null;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    public void Clear(HttpContext context) => context.Response.Cookies.Delete(Name,
        new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/" });
}

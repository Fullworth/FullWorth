namespace FullWorth.Core.Security;

public static class AuthenticationSecurityDefaults
{
    public static readonly TimeSpan
        AccessTokenLifetime =
            TimeSpan.FromMinutes(15);

    public static readonly TimeSpan
        RefreshTokenLifetime =
            TimeSpan.FromDays(7);

    /*
     * A persistent Web session must not outlive the API refresh credential
     * that backs it. If the shared server-side session cache is lost,
     * sessions fail closed and users sign in again.
     */
    public static readonly TimeSpan
        PersistentWebSessionLifetime =
            RefreshTokenLifetime;

    public static readonly TimeSpan
        BrowserSessionMaximumLifetime =
            TimeSpan.FromHours(12);
}

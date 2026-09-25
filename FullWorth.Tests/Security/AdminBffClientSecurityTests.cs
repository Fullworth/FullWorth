namespace FullWorth.Tests.Security;

public sealed class AdminBffClientSecurityTests
{
    [Fact]
    public void ReauthenticationFailures_DoNotMasqueradeAsExpiredSessions()
    {
        var script =
            File.ReadAllText(
                Path.Combine(
                    FindRepositoryRoot(),
                    "FullWorth.Web",
                    "wwwroot",
                    "js",
                    "admin-bff.js"));

        Assert.Contains(
            "isCredentialReauthenticationFailure",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "current password is incorrect.",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "a current authenticator code is required.",
            script,
            StringComparison.Ordinal);

        Assert.Contains(
            "the authenticator code is invalid.",
            script,
            StringComparison.Ordinal);

        var credentialFailureIndex =
            script.IndexOf(
                "await isCredentialReauthenticationFailure(response)",
                StringComparison.Ordinal);

        var loginRedirectIndex =
            script.IndexOf(
                "window.location.assign(\"/login\")",
                StringComparison.Ordinal);

        Assert.True(
            credentialFailureIndex >= 0 &&
            loginRedirectIndex > credentialFailureIndex,
            "Expected credential reauthentication failures to be handled before the session-expired redirect.");
    }

    private static string FindRepositoryRoot()
    {
        var directory =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "FullWorth.slnx")) &&
                Directory.Exists(
                    Path.Combine(
                        directory.FullName,
                        "FullWorth.Web")))
            {
                return directory.FullName;
            }

            directory =
                directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the FullWorth repository root.");
    }
}

using FullWorth.API.Infrastructure;

namespace FullWorth.Tests.Security;

public sealed class ProductionDatabaseConnectionSecurityTests
{
    [Fact]
    public void Development_AllowsUnverifiedLocalConnection()
    {
        ProductionDatabaseConnectionSecurity.Validate(
            "Host=localhost;Database=billwatch;Username=dev;Password=dev",
            isDevelopment:
                true);
    }

    [Fact]
    public void Production_InternalDockerDatabase_AllowsPrivateNetworkConnection()
    {
        ProductionDatabaseConnectionSecurity.Validate(
            "Host=database;Port=5432;Database=billwatch;Username=billwatch;Password=test-password",
            isDevelopment:
                false);
    }

    [Theory]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    public void Production_NonLocalDatabase_RejectsTlsModesWithoutHostnameVerification(
        string sslMode)
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    ProductionDatabaseConnectionSecurity.Validate(
                        $"Host=db.fullworth.test;Database=billwatch;Username=fullworth;Password=test-password;SSL Mode={sslMode}",
                        isDevelopment:
                            false));

        Assert.Contains(
            "VerifyFull",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Production_NonLocalDatabase_AllowsVerifyFull()
    {
        ProductionDatabaseConnectionSecurity.Validate(
            "Host=db.fullworth.test;Database=billwatch;Username=fullworth;Password=test-password;SSL Mode=VerifyFull",
            isDevelopment:
                false);
    }

    [Fact]
    public void Production_NonLocalDatabase_RejectsTrustedServerCertificate()
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    ProductionDatabaseConnectionSecurity.Validate(
                        "Host=db.fullworth.test;Database=billwatch;Username=fullworth;Password=test-password;SSL Mode=VerifyFull;Trust Server Certificate=true",
                        isDevelopment:
                            false));

        Assert.Contains(
            "validate",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_RejectsImplicitDatabaseUsername()
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    ProductionDatabaseConnectionSecurity.Validate(
                        "Host=database;Database=billwatch;Password=test-password",
                        isDevelopment:
                            false));

        Assert.Contains(
            "username",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_RejectsImplicitDatabaseAuthentication()
    {
        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    ProductionDatabaseConnectionSecurity.Validate(
                        "Host=database;Database=billwatch;Username=billwatch",
                        isDevelopment:
                            false));

        Assert.Contains(
            "password authentication",
            exception.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_InvalidConnectionString_DoesNotEchoSecret()
    {
        const string secret =
            "do-not-echo-this-database-secret";

        var exception =
            Assert.Throws<InvalidOperationException>(
                () =>
                    ProductionDatabaseConnectionSecurity.Validate(
                        $"Host=database;Database=billwatch;Username=billwatch;Password={secret};DefinitelyNotASetting=value",
                        isDevelopment:
                            false));

        Assert.DoesNotContain(
            secret,
            exception.Message,
            StringComparison.Ordinal);
    }
}

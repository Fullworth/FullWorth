using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using FullWorth.API.Services.Statements;

public sealed class ParserWorkerTlsTests
{
    [Fact]
    public void PublishedCertificate_IsPublicOnly_AndExactPinIsAccepted()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "parser-worker.cer.pem");
        using var certificate =
            ParserWorkerTlsCertificate.CreateAndPublish(path);
        var validator =
            new ParserWorkerServerCertificateValidator(path);

        using var published =
            X509CertificateLoader.LoadCertificateFromFile(
                path);

        Assert.True(certificate.HasPrivateKey);
        Assert.False(published.HasPrivateKey);
        Assert.True(validator.Validate(
            new HttpRequestMessage(),
            certificate,
            null,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void ExactPin_RejectsAnotherCertificate()
    {
        using var firstDirectory = new TemporaryDirectory();
        using var secondDirectory = new TemporaryDirectory();
        var firstPath = Path.Combine(
            firstDirectory.Path,
            "parser-worker.cer.pem");
        var secondPath = Path.Combine(
            secondDirectory.Path,
            "parser-worker.cer.pem");
        using var expected =
            ParserWorkerTlsCertificate.CreateAndPublish(firstPath);
        using var other =
            ParserWorkerTlsCertificate.CreateAndPublish(secondPath);
        var validator =
            new ParserWorkerServerCertificateValidator(firstPath);

        Assert.True(validator.Validate(
            new HttpRequestMessage(),
            expected,
            null,
            SslPolicyErrors.None));
        Assert.False(validator.Validate(
            new HttpRequestMessage(),
            other,
            null,
            SslPolicyErrors.None));
    }

    [Fact]
    public void ExactPin_FollowsAtomicWorkerCertificateRotation()
    {
        using var temporary = new TemporaryDirectory();
        var path = Path.Combine(temporary.Path, "parser-worker.cer.pem");
        using var original =
            ParserWorkerTlsCertificate.CreateAndPublish(path);
        var validator =
            new ParserWorkerServerCertificateValidator(path);

        using var replacement =
            ParserWorkerTlsCertificate.CreateAndPublish(path);

        Assert.False(validator.Validate(
            new HttpRequestMessage(),
            original,
            null,
            SslPolicyErrors.None));
        Assert.True(validator.Validate(
            new HttpRequestMessage(),
            replacement,
            null,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void TlsConfiguration_RejectsRelativeOrMissingPaths()
    {
        Assert.Throws<InvalidOperationException>(
            () => ParserWorkerTlsCertificate.CreateAndPublish(
                "parser-worker.cer.pem"));
        Assert.Throws<InvalidOperationException>(
            () => new ParserWorkerServerCertificateValidator(
                Path.Combine(
                    Path.GetTempPath(),
                    Guid.NewGuid().ToString("N"),
                    "missing.pem")));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "fullworth-parser-tls-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

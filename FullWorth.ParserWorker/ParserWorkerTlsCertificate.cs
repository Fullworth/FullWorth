using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public static class ParserWorkerTlsCertificate
{
    private const string ServerAuthenticationOid =
        "1.3.6.1.5.5.7.3.1";

    public static X509Certificate2 CreateAndPublish(
        string? publicCertificatePath)
    {
        if (string.IsNullOrWhiteSpace(publicCertificatePath) ||
            !Path.IsPathFullyQualified(publicCertificatePath))
        {
            throw new InvalidOperationException(
                "ParserWorker:TlsCertificatePath must be an absolute path.");
        }

        var directory = Path.GetDirectoryName(publicCertificatePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(
                "The parser worker TLS certificate directory is invalid.");
        }

        Directory.CreateDirectory(directory);

        using var key = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=parser-worker",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature,
                critical: true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection
                {
                    new(ServerAuthenticationOid)
                },
                critical: true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(
                request.PublicKey,
                critical: false));

        var subjectAlternativeNames =
            new SubjectAlternativeNameBuilder();
        subjectAlternativeNames.AddDnsName("parser-worker");
        subjectAlternativeNames.AddDnsName("localhost");
        subjectAlternativeNames.AddIpAddress(IPAddress.Loopback);
        subjectAlternativeNames.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(
            subjectAlternativeNames.Build(critical: true));

        var now = DateTimeOffset.UtcNow;
        using var generated = request.CreateSelfSigned(
            now.AddMinutes(-5),
            now.AddDays(2));
        var certificate = new X509Certificate2(
            generated.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.EphemeralKeySet);

        var temporaryPath =
            $"{publicCertificatePath}.{Convert.ToHexString(
                RandomNumberGenerator.GetBytes(8)).ToLowerInvariant()}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                certificate.ExportCertificatePem());
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.GroupRead);
            }

            File.Move(
                temporaryPath,
                publicCertificatePath,
                overwrite: true);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return certificate;
    }
}

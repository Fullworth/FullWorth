using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FullWorth.API.Services.Statements;

public sealed class ParserWorkerServerCertificateValidator
{
    private readonly string _certificatePath;

    public ParserWorkerServerCertificateValidator(
        string? certificatePath)
    {
        if (string.IsNullOrWhiteSpace(certificatePath) ||
            !Path.IsPathFullyQualified(certificatePath))
        {
            throw new InvalidOperationException(
                "ParserWorker:ServerCertificatePath must reference an absolute certificate path.");
        }

        _certificatePath = certificatePath;

        /*
         * The parser worker publishes an ephemeral public certificate during
         * startup and replaces it atomically on rotation. The API may start
         * before that first publication, so a missing pin is not a
         * configuration error. Validation remains fail-closed until a valid
         * public-only pin is present.
         */
        if (File.Exists(_certificatePath))
        {
            EnsurePublicOnlyPin(_certificatePath);
        }
    }

    public bool Validate(
        HttpRequestMessage _,
        X509Certificate2? certificate,
        X509Chain? __,
        SslPolicyErrors ___)
    {
        if (certificate is null)
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (now < certificate.NotBefore.ToUniversalTime() ||
            now > certificate.NotAfter.ToUniversalTime())
        {
            return false;
        }

        try
        {
            using var expected =
                X509CertificateLoader.LoadCertificateFromFile(
                    _certificatePath);

            if (expected.HasPrivateKey)
            {
                return false;
            }

            var expectedHash =
                SHA256.HashData(expected.RawData);
            var actualHash =
                SHA256.HashData(certificate.RawData);

            return CryptographicOperations.FixedTimeEquals(
                actualHash,
                expectedHash);
        }
        catch
        {
            return false;
        }
    }

    private static void EnsurePublicOnlyPin(
        string certificatePath)
    {
        using var expected =
            X509CertificateLoader.LoadCertificateFromFile(
                certificatePath);

        if (expected.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "The parser worker public certificate pin must not contain a private key.");
        }
    }
}

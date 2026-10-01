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
            !Path.IsPathFullyQualified(certificatePath) ||
            !File.Exists(certificatePath))
        {
            throw new InvalidOperationException(
                "ParserWorker:ServerCertificatePath must reference an existing absolute certificate path.");
        }

        _certificatePath = certificatePath;
        using var expected =
            X509Certificate2.CreateFromPem(
                File.ReadAllText(_certificatePath));
        if (expected.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "The parser worker public certificate pin must not contain a private key.");
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
                X509Certificate2.CreateFromPem(
                    File.ReadAllText(_certificatePath));
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
}

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

public sealed class ParserWorkerAuthentication
{
    private const string DevelopmentToken =
        "fullworth-parser-worker-development-only-token";
    private readonly byte[] _expectedTokenHash;

    public ParserWorkerAuthentication(
        string? configuredToken,
        bool isDevelopment)
    {
        var token =
            string.IsNullOrEmpty(configuredToken) && isDevelopment
                ? DevelopmentToken
                : configuredToken;

        if (string.IsNullOrEmpty(token) ||
            token.Length < 32 ||
            token.Length > 512 ||
            token.Any(char.IsWhiteSpace) ||
            token.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Parser worker authentication is not configured securely.");
        }

        _expectedTokenHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(token));
    }

    public bool IsAuthorized(StringValues authorizationValues)
    {
        if (authorizationValues.Count != 1)
        {
            return false;
        }

        var authorization = authorizationValues[0];
        const string prefix = "Bearer ";
        if (authorization is null ||
            !authorization.StartsWith(prefix, StringComparison.Ordinal) ||
            authorization.Length <= prefix.Length ||
            authorization.Length - prefix.Length > 512)
        {
            return false;
        }

        var candidateHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    authorization[prefix.Length..]));

        return CryptographicOperations.FixedTimeEquals(
            candidateHash,
            _expectedTokenHash);
    }
}

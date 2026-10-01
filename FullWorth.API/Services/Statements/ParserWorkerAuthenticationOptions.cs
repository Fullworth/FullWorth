using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

namespace FullWorth.API.Services.Statements;

public sealed class ParserWorkerAuthenticationOptions
{
    internal const string DevelopmentToken =
        "fullworth-parser-worker-development-only-token";
    internal const string TimestampHeaderName =
        "X-FullWorth-Parser-Timestamp";
    internal const string NonceHeaderName =
        "X-FullWorth-Parser-Nonce";
    internal const string SignatureHeaderName =
        "X-FullWorth-Parser-Signature";

    public ParserWorkerAuthenticationOptions(
        string? configuredToken,
        bool isDevelopment)
    {
        Token = Validate(
            string.IsNullOrEmpty(configuredToken) && isDevelopment
                ? DevelopmentToken
                : configuredToken);
    }

    public string Token { get; }

    public void ApplyTo(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var timestamp =
            DateTimeOffset.UtcNow
                .ToUnixTimeSeconds()
                .ToString(CultureInfo.InvariantCulture);
        var nonce =
            Convert.ToHexString(RandomNumberGenerator.GetBytes(16))
                .ToLowerInvariant();
        var requestPath =
            request.RequestUri is { IsAbsoluteUri: true } absoluteUri
                ? absoluteUri.AbsolutePath
                : request.RequestUri?.OriginalString ?? string.Empty;
        var signature = CreateSignature(
            request.Method.Method,
            requestPath,
            timestamp,
            nonce);

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", Token);
        request.Headers.Add(TimestampHeaderName, timestamp);
        request.Headers.Add(NonceHeaderName, nonce);
        request.Headers.Add(SignatureHeaderName, signature);
    }

    private string CreateSignature(
        string method,
        string path,
        string timestamp,
        string nonce)
    {
        var canonicalRequest =
            $"{method}\n{path}\n{timestamp}\n{nonce}";
        var signature =
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(Token),
                Encoding.UTF8.GetBytes(canonicalRequest));
        return Convert.ToHexString(signature).ToLowerInvariant();
    }

    private static string Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) ||
            token.Length < 32 ||
            token.Length > 512 ||
            token.Any(char.IsWhiteSpace) ||
            token.Any(char.IsControl))
        {
            throw new InvalidOperationException(
                "Parser worker authentication is not configured securely.");
        }

        return token;
    }
}

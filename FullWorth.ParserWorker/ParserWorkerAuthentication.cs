using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Primitives;

public sealed class ParserWorkerAuthentication
{
    public const string TimestampHeaderName =
        "X-FullWorth-Parser-Timestamp";
    public const string NonceHeaderName =
        "X-FullWorth-Parser-Nonce";
    public const string ContentSha256HeaderName =
        "X-FullWorth-Parser-Content-SHA256";
    public const string SignatureHeaderName =
        "X-FullWorth-Parser-Signature";

    private const string DevelopmentToken =
        "fullworth-parser-worker-development-only-token";
    private const long MaxClockSkewSeconds = 60;
    private const int MaxTrackedNonces = 4096;

    private readonly byte[] _expectedTokenHash;
    private readonly byte[] _hmacKey;
    private readonly TimeProvider _timeProvider;
    private readonly object _replayGate = new();
    private readonly Dictionary<string, long> _acceptedNonces =
        new(StringComparer.Ordinal);

    public ParserWorkerAuthentication(
        string? configuredToken,
        bool isDevelopment,
        TimeProvider? timeProvider = null)
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

        _hmacKey = Encoding.UTF8.GetBytes(token);
        _expectedTokenHash = SHA256.HashData(_hmacKey);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public bool IsAuthorized(
        StringValues authorizationValues,
        StringValues timestampValues,
        StringValues nonceValues,
        StringValues contentSha256Values,
        StringValues signatureValues,
        string method,
        string path)
    {
        if (!TryGetSingle(authorizationValues, out var authorization))
        {
            return false;
        }

        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal) ||
            authorization.Length <= prefix.Length ||
            authorization.Length - prefix.Length > 512)
        {
            return false;
        }

        var candidateHash =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    authorization[prefix.Length..]));
        if (!CryptographicOperations.FixedTimeEquals(
                candidateHash,
                _expectedTokenHash))
        {
            return false;
        }

        if (!TryGetSingle(timestampValues, out var timestampText) ||
            timestampText.Length > 12 ||
            !long.TryParse(
                timestampText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var timestamp) ||
            !TryGetSingle(nonceValues, out var nonce) ||
            !IsLowerHex(nonce, 32) ||
            !TryGetSingle(contentSha256Values, out var contentSha256) ||
            !IsLowerHex(contentSha256, 64) ||
            !TryGetSingle(signatureValues, out var signatureText) ||
            !IsLowerHex(signatureText, 64))
        {
            return false;
        }

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (timestamp < now - MaxClockSkewSeconds ||
            timestamp > now + MaxClockSkewSeconds)
        {
            return false;
        }

        var canonicalRequest =
            $"{method}\n{path}\n{timestampText}\n{nonce}\n{contentSha256}";
        var expectedSignature =
            HMACSHA256.HashData(
                _hmacKey,
                Encoding.UTF8.GetBytes(canonicalRequest));
        var candidateSignature = Convert.FromHexString(signatureText);
        if (!CryptographicOperations.FixedTimeEquals(
                candidateSignature,
                expectedSignature))
        {
            return false;
        }

        lock (_replayGate)
        {
            foreach (var expiredNonce in
                _acceptedNonces
                    .Where(entry => entry.Value < now)
                    .Select(entry => entry.Key)
                    .ToArray())
            {
                _acceptedNonces.Remove(expiredNonce);
            }

            if (_acceptedNonces.Count >= MaxTrackedNonces ||
                !_acceptedNonces.TryAdd(
                    nonce,
                    timestamp + MaxClockSkewSeconds))
            {
                return false;
            }
        }

        return true;
    }

    public bool HasExpectedContentHash(
        StringValues contentSha256Values,
        ReadOnlySpan<byte> content)
    {
        if (!TryGetSingle(
                contentSha256Values,
                out var contentSha256) ||
            !IsLowerHex(contentSha256, 64))
        {
            return false;
        }

        var expectedHash = Convert.FromHexString(contentSha256);
        var actualHash = SHA256.HashData(content);
        return CryptographicOperations.FixedTimeEquals(
            actualHash,
            expectedHash);
    }

    private static bool TryGetSingle(
        StringValues values,
        out string value)
    {
        value = string.Empty;
        if (values.Count != 1 ||
            string.IsNullOrEmpty(values[0]))
        {
            return false;
        }

        value = values[0]!;
        return true;
    }

    private static bool IsLowerHex(string value, int expectedLength) =>
        value.Length == expectedLength &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

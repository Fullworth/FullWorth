using System.Net.Http.Headers;

namespace FullWorth.API.Services.Statements;

public sealed class ParserWorkerAuthenticationOptions
{
    internal const string DevelopmentToken =
        "fullworth-parser-worker-development-only-token";

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

    public AuthenticationHeaderValue CreateHeader() =>
        new("Bearer", Token);

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

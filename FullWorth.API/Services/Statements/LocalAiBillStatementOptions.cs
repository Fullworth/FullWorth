using Microsoft.Extensions.Options;

namespace FullWorth.API.Services.Statements;

public static class BillStatementAiPromptVersions
{
    public const string V1 =
        "bill-statement-extraction-v1";

    public const string V2 =
        "bill-statement-extraction-v2";

    public static bool IsSupported(string? promptVersion)
    {
        return promptVersion is V1 or V2;
    }
}

public sealed class LocalAiBillStatementOptions
{
    public const string SectionName =
        "StatementAi:Local";

    public bool Enabled { get; set; }

    public string Model { get; set; } =
        "fullworth-local";

    public string? ApiKey { get; set; }

    public string Endpoint { get; set; } =
        "http://127.0.0.1:8080/v1/chat/completions";

    public string PromptVersion { get; set; } =
        BillStatementAiPromptVersions.V2;

    public int MaxDocumentCharacters { get; set; } =
        40_000;

    public int MaxOutputTokens { get; set; } =
        4_000;

    public int TimeoutSeconds { get; set; } =
        90;
}

public sealed class LocalAiBillStatementOptionsValidator
    : IValidateOptions<LocalAiBillStatementOptions>
{
    public ValidateOptionsResult Validate(
        string? name,
        LocalAiBillStatementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures =
            new List<string>();

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            failures.Add(
                "StatementAi:Local:Model is required.");
        }

        if (!Uri.TryCreate(
                options.Endpoint,
                UriKind.Absolute,
                out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp &&
             endpoint.Scheme != Uri.UriSchemeHttps) ||
            !endpoint.IsLoopback ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) ||
            !string.Equals(
                endpoint.AbsolutePath.TrimEnd('/'),
                "/v1/chat/completions",
                StringComparison.Ordinal))
        {
            failures.Add(
                "StatementAi:Local:Endpoint must be a loopback HTTP(S) /v1/chat/completions endpoint.");
        }

        if (!BillStatementAiPromptVersions.IsSupported(
                options.PromptVersion))
        {
            failures.Add(
                "StatementAi:Local:PromptVersion must be a supported, versioned prompt.");
        }

        if (options.MaxDocumentCharacters is < 1_000 or > 200_000)
        {
            failures.Add(
                "StatementAi:Local:MaxDocumentCharacters must be between 1,000 and 200,000.");
        }

        if (options.MaxOutputTokens is < 500 or > 16_000)
        {
            failures.Add(
                "StatementAi:Local:MaxOutputTokens must be between 500 and 16,000.");
        }

        if (options.TimeoutSeconds is < 5 or > 300)
        {
            failures.Add(
                "StatementAi:Local:TimeoutSeconds must be between 5 and 300.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

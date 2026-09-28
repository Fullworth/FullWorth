using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace FullWorth.API.Services.Statements;

public sealed class LocalAiBillStatementAiExtractor
    : IBillStatementAiExtractor,
      IBillStatementAiInferenceCallCounter
{
    private const int MaxResponseBytes =
        1_048_576;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive =
                true
        };

    private static readonly JsonObject OutputSchema =
        CreateOutputSchema();

    private readonly HttpClient _httpClient;

    private readonly LocalAiBillStatementOptions _options;

    private long _inferenceCallCount;

    static LocalAiBillStatementAiExtractor()
    {
        JsonOptions.Converters.Add(
            new JsonStringEnumConverter());
    }

    public LocalAiBillStatementAiExtractor(
        HttpClient httpClient,
        IOptions<LocalAiBillStatementOptions> options)
    {
        _httpClient =
            httpClient ??
            throw new ArgumentNullException(nameof(httpClient));

        _options =
            options?.Value ??
            throw new ArgumentNullException(nameof(options));
    }

    public long InferenceCallCount =>
        Interlocked.Read(
            ref _inferenceCallCount);

    public async Task<BillStatementAiCandidate> ExtractAsync(
        BillStatementAiExtractionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.Enabled)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.Disabled,
                "Local AI statement extraction is disabled.");
        }

        if (string.IsNullOrWhiteSpace(request.DocumentText))
        {
            throw new ArgumentException(
                "Document text is required.",
                nameof(request));
        }

        if (!string.Equals(
                request.PromptVersion,
                _options.PromptVersion,
                StringComparison.Ordinal))
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.Configuration,
                "The requested prompt version is not configured.");
        }

        if (!LocalAiBillStatementPromptCatalog.IsSupported(
                _options.PromptVersion))
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.Configuration,
                "The configured prompt version is not supported.");
        }

        if (request.DocumentText.Length >
            _options.MaxDocumentCharacters)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.InputTooLarge,
                "Statement text exceeds the configured local AI input limit.");
        }

        string validatedDocumentText =
            request.DocumentText;

        using var timeoutSource =
            new CancellationTokenSource(
                TimeSpan.FromSeconds(
                    _options.TimeoutSeconds));

        using var linkedSource =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutSource.Token);

        using var httpRequest =
            new HttpRequestMessage(
                HttpMethod.Post,
                _options.Endpoint)
            {
                Content =
                    JsonContent.Create(
                        CreateRequestBody(
                            request,
                            validatedDocumentText))
            };

        if (!string.IsNullOrWhiteSpace(
                _options.ApiKey))
        {
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    _options.ApiKey);
        }

        try
        {
            linkedSource.Token.ThrowIfCancellationRequested();

            Interlocked.Increment(
                ref _inferenceCallCount);

            using HttpResponseMessage response =
                await _httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    linkedSource.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new BillStatementAiExtractionException(
                    BillStatementAiExtractionFailureKind.HttpStatus,
                    $"Local AI statement extraction failed with HTTP {(int)response.StatusCode}.");
            }

            byte[] responseBytes =
                await ReadBoundedResponseAsync(
                    response.Content,
                    linkedSource.Token);

            using JsonDocument responseJson =
                JsonDocument.Parse(
                    responseBytes);

            string outputText =
                FindOutputText(
                    responseJson.RootElement)
                ?? throw new BillStatementAiExtractionException(
                    BillStatementAiExtractionFailureKind.MissingStructuredOutput,
                    "Local AI returned no structured statement output.");

            return JsonSerializer.Deserialize<
                    BillStatementAiCandidate>(
                    outputText,
                    JsonOptions)
                ?? throw new BillStatementAiExtractionException(
                    BillStatementAiExtractionFailureKind.MissingStructuredOutput,
                    "Local AI returned an empty structured statement candidate.");
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.Timeout,
                "Local AI statement extraction timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.Transport,
                "Local AI statement extraction could not reach the local model.",
                exception);
        }
        catch (IOException exception)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.ResponseRead,
                "Local AI returned an unreadable response.",
                exception);
        }
        catch (JsonException exception)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.InvalidStructuredOutput,
                "Local AI returned invalid structured statement output.",
                exception);
        }
    }

    private JsonObject CreateRequestBody(
        BillStatementAiExtractionRequest request,
        string documentText)
    {
        string systemInstructions =
            LocalAiBillStatementPromptCatalog
                .GetSystemInstructions(
                    _options.PromptVersion);

        return new JsonObject
        {
            ["model"] =
                _options.Model,

            ["stream"] =
                false,

            ["temperature"] =
                0,

            ["max_tokens"] =
                _options.MaxOutputTokens,

            ["messages"] =
                new JsonArray
                {
                    new JsonObject
                    {
                        ["role"] =
                            "system",

                        ["content"] =
                            $"{systemInstructions}\nPrompt version: {_options.PromptVersion}"
                    },

                    new JsonObject
                    {
                        ["role"] =
                            "user",

                        ["content"] =
                            CreateUserInput(
                                request,
                                documentText)
                    }
                },

            ["response_format"] =
                new JsonObject
                {
                    ["type"] =
                        "json_schema",

                    ["json_schema"] =
                        new JsonObject
                        {
                            ["name"] =
                                "bill_statement_candidate",

                            ["strict"] =
                                true,

                            ["schema"] =
                                OutputSchema.DeepClone()
                        }
                }
        };
    }

    private static string CreateUserInput(
        BillStatementAiExtractionRequest request,
        string documentText)
    {
        return $$"""
            EXPECTED PROVIDER HINT: {{request.Hints.ExpectedProviderName ?? "none"}}
            EXPECTED CATEGORY HINT: {{request.Hints.ExpectedCategory ?? "none"}}

            STATEMENT TEXT:
            {{documentText}}
            """;
    }

    private static string? FindOutputText(
        JsonElement root)
    {
        if (!root.TryGetProperty(
                "choices",
                out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return null;
        }

        JsonElement choice =
            choices[0];

        if (choice.ValueKind !=
                JsonValueKind.Object ||
            !choice.TryGetProperty(
                "finish_reason",
                out JsonElement finishReason) ||
            finishReason.ValueKind !=
                JsonValueKind.String ||
            !string.Equals(
                finishReason.GetString(),
                "stop",
                StringComparison.Ordinal))
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.IncompleteResponse,
                "Local AI did not complete the structured statement response.");
        }

        if (!choice.TryGetProperty(
                "message",
                out JsonElement message) ||
            message.ValueKind !=
                JsonValueKind.Object ||
            !message.TryGetProperty(
                "content",
                out JsonElement content) ||
            content.ValueKind !=
                JsonValueKind.String)
        {
            return null;
        }

        return content.GetString();
    }

    private static async Task<byte[]> ReadBoundedResponseAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (content.Headers.ContentLength is > MaxResponseBytes)
        {
            throw new BillStatementAiExtractionException(
                BillStatementAiExtractionFailureKind.OversizedResponse,
                "Local AI returned an oversized response.");
        }

        await using Stream stream =
            await content.ReadAsStreamAsync(
                cancellationToken);

        using var buffer =
            new MemoryStream();

        byte[] chunk =
            new byte[8_192];

        while (true)
        {
            int read =
                await stream.ReadAsync(
                    chunk.AsMemory(),
                    cancellationToken);

            if (read == 0)
                break;

            if (buffer.Length + read >
                MaxResponseBytes)
            {
                throw new BillStatementAiExtractionException(
                    BillStatementAiExtractionFailureKind.OversizedResponse,
                    "Local AI returned an oversized response.");
            }

            await buffer.WriteAsync(
                chunk.AsMemory(
                    0,
                    read),
                cancellationToken);
        }

        return buffer.ToArray();
    }

    private static JsonObject CreateOutputSchema()
    {
        static JsonObject Nullable(
            string type)
        {
            return new JsonObject
            {
                ["type"] =
                    new JsonArray(
                        type,
                        "null")
            };
        }

        var properties =
            new JsonObject
            {
                ["providerName"] =
                    Nullable("string"),

                ["accountIdentifierSuffix"] =
                    Nullable("string"),

                ["billingPeriodStart"] =
                    Nullable("string"),

                ["billingPeriodEnd"] =
                    Nullable("string"),

                ["statementDate"] =
                    Nullable("string"),

                ["dueDate"] =
                    Nullable("string"),

                ["previousBalance"] =
                    Nullable("number"),

                ["payments"] =
                    Nullable("number"),

                ["currentCharges"] =
                    Nullable("number"),

                ["totalDue"] =
                    Nullable("number"),

                ["currencyCode"] =
                    Nullable("string"),

                ["planOrService"] =
                    Nullable("string"),

                ["usageSummary"] =
                    Nullable("string"),

                ["lineItems"] =
                    ArrayOfObject(
                        new JsonObject
                        {
                            ["description"] =
                                Type("string"),

                            ["amount"] =
                                Type("number"),

                            ["kind"] =
                                EnumNames<
                                    BillStatementAiLineItemKind>()
                        }),

                ["evidence"] =
                    ArrayOfObject(
                        new JsonObject
                        {
                            ["factKey"] =
                                Type("string"),

                            ["sourceExcerpt"] =
                                Type("string"),

                            ["pageNumber"] =
                                Nullable("integer")
                        }),

                ["modelConfidence"] =
                    EnumNames<
                        BillStatementAiModelConfidence>()
            };

        return ObjectSchema(properties);
    }

    private static JsonObject Type(
        string type)
    {
        return new JsonObject
        {
            ["type"] =
                type
        };
    }

    private static JsonObject EnumNames<TEnum>()
        where TEnum : struct, Enum
    {
        return new JsonObject
        {
            ["type"] =
                "string",

            ["enum"] =
                new JsonArray(
                    Enum.GetNames<TEnum>()
                        .Select(
                            enumName =>
                                (JsonNode?)JsonValue.Create(
                                    enumName))
                        .ToArray<JsonNode?>())
        };
    }

    private static JsonObject ArrayOfObject(
        JsonObject properties)
    {
        return new JsonObject
        {
            ["type"] =
                "array",

            ["items"] =
                ObjectSchema(properties)
        };
    }

    private static JsonObject ObjectSchema(
        JsonObject properties)
    {
        return new JsonObject
        {
            ["type"] =
                "object",

            ["additionalProperties"] =
                false,

            ["properties"] =
                properties,

            ["required"] =
                new JsonArray(
                    properties.Select(
                            property =>
                                JsonValue.Create(
                                    property.Key))
                        .ToArray<JsonNode?>())
        };
    }
}

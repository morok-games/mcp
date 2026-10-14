using System.ComponentModel;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Options;
using Platform.Api.Models;

namespace Platform.Api.Services;

/// <summary>
/// Minimal Gemini REST API client (models/{model}:generateContent) without external SDKs.
/// </summary>
public class GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> logger) : ILlmClient
{
    private readonly GeminiOptions _options = options.Value;

    public Task<string> GenerateAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        var request = new GeminiRequest(
            Contents: history
                .Select(m => new GeminiContent(
                    // Gemini only supports the "user" and "model" roles
                    Role: m.Role == "assistant" ? "model" : "user",
                    Parts: [new GeminiPart(m.Text)]))
                .ToList(),
            SystemInstruction: new GeminiContent(null, [new GeminiPart(_options.SystemPrompt)]));

        return GenerateCoreAsync(request, ct);
    }

    public async Task<T> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(StructuredJsonOptions, typeof(T),
            new JsonSchemaExporterOptions
            {
                TreatNullObliviousAsNonNullable = true,
                // [Description] attributes become schema descriptions: they act as per-field instructions for the model
                TransformSchemaNode = (context, node) =>
                {
                    var provider = context.PropertyInfo?.AttributeProvider ?? context.TypeInfo.Type;
                    var description = provider.GetCustomAttributes(typeof(DescriptionAttribute), inherit: true)
                        .OfType<DescriptionAttribute>().FirstOrDefault()?.Description;

                    if (description is not null && node is JsonObject obj)
                        obj["description"] = description;

                    return node;
                }
            });

        var request = new GeminiRequest(
            Contents: [new GeminiContent("user", [new GeminiPart(userPrompt)])],
            SystemInstruction: new GeminiContent(null, [new GeminiPart(systemPrompt)]),
            GenerationConfig: new GeminiGenerationConfig(
                ResponseMimeType: "application/json",
                ResponseJsonSchema: schema,
                Temperature: 0));

        var json = await GenerateCoreAsync(request, ct);

        return JsonSerializer.Deserialize<T>(json, StructuredJsonOptions)
               ?? throw new InvalidOperationException("Gemini returned an empty structured response.");
    }

    private async Task<string> GenerateCoreAsync(GeminiRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");

        // The free tier is frequently overloaded per model (503/429/500), and old models get
        // retired (404), so try the primary model first and then fall back to the others.
        var models = new[] { _options.Model }.Concat(_options.FallbackModels).Distinct().ToList();
        var errors = new List<string>();

        foreach (var model in models)
        {
            var (status, body) = await SendWithRetryAsync(model, request, ct);

            if (status == HttpStatusCode.OK)
            {
                if (errors.Count > 0)
                    logger.LogInformation("Gemini fallback model {Model} answered after: {Errors}", model, string.Join(", ", errors));

                var result = JsonSerializer.Deserialize<GeminiResponse>(body, JsonOptions);
                var text = string.Concat(result?.Candidates?.FirstOrDefault()?.Content?.Parts?.Select(p => p.Text) ?? []);
                return string.IsNullOrWhiteSpace(text) ? "(empty response)" : text;
            }

            logger.LogWarning("Gemini model {Model} failed with {Status}: {Body}", model, (int)status, body);
            errors.Add($"{model}: {(int)status}");

            if (!IsTransient(status) && status != HttpStatusCode.NotFound)
                break; // e.g. 400/403: a request or key problem that another model will not fix
        }

        throw new HttpRequestException($"Gemini is unavailable ({string.Join(", ", errors)}). Please try again later.");
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests or HttpStatusCode.InternalServerError;

    /// <summary>
    /// Sends the request to one model, retrying transient errors with a short backoff.
    /// </summary>
    private async Task<(HttpStatusCode Status, string Body)> SendWithRetryAsync(string model, GeminiRequest request, CancellationToken ct)
    {
        const int maxAttempts = 2;

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            message.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!IsTransient(response.StatusCode) || attempt == maxAttempts)
                return (response.StatusCode, body);

            logger.LogWarning("Gemini model {Model} returned {Status}, retrying (attempt {Attempt}/{Max})",
                model, (int)response.StatusCode, attempt, maxAttempts);
            await Task.Delay(TimeSpan.FromSeconds(1), ct);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Options shared by schema generation and deserialization so enum names and casing match.</summary>
    private static readonly JsonSerializerOptions StructuredJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private record GeminiRequest(
        List<GeminiContent> Contents,
        GeminiContent? SystemInstruction,
        GeminiGenerationConfig? GenerationConfig = null);
    private record GeminiGenerationConfig(string? ResponseMimeType, JsonNode? ResponseJsonSchema, double? Temperature);
    private record GeminiContent(string? Role, List<GeminiPart>? Parts);
    private record GeminiPart(string? Text);
    private record GeminiResponse(List<GeminiCandidate>? Candidates);
    private record GeminiCandidate(GeminiContent? Content);
}

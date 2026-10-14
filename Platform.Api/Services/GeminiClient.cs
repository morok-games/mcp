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
public class GeminiClient(
    HttpClient http,
    IOptions<GeminiOptions> options,
    ModelCircuitBreaker breaker,
    ILogger<GeminiClient> logger) : ILlmClient
{
    private readonly GeminiOptions _options = options.Value;

    public Task<string> GenerateAsync(IReadOnlyList<ChatMessage> history, string? systemPrompt = null, CancellationToken ct = default)
    {
        var request = new GeminiRequest(
            Contents: history
                .Select(m => new GeminiContent(
                    // Gemini only supports the "user" and "model" roles
                    Role: m.Role == "assistant" ? "model" : "user",
                    Parts: [new GeminiPart(m.Text)]))
                .ToList(),
            SystemInstruction: new GeminiContent(null, [new GeminiPart(systemPrompt ?? _options.SystemPrompt)]));

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
        var configured = new[] { _options.Model }.Concat(_options.FallbackModels).Distinct().ToList();

        // Skip models that failed recently; if all of them did, trying is still better than failing outright
        var models = configured.Where(m => !breaker.IsOpen(m)).ToList();
        if (models.Count == 0)
        {
            logger.LogWarning("All Gemini models are in circuit break, trying all of them");
            models = configured;
        }
        else if (models.Count < configured.Count)
        {
            logger.LogInformation("Skipping Gemini models in circuit break: {Models}",
                string.Join(", ", configured.Except(models)));
        }

        var errors = new List<string>();

        for (var i = 0; i < models.Count; i++)
        {
            var model = models[i];
            // Retrying the same model only pays off when there is no other model to fall back to
            var isLast = i == models.Count - 1;
            var (status, body) = await SendWithRetryAsync(model, request, maxAttempts: isLast ? 2 : 1, ct);

            if (status == HttpStatusCode.OK)
            {
                breaker.Close(model);
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

            breaker.Open(model, TimeSpan.FromSeconds(_options.CircuitBreakSeconds));
            logger.LogWarning("Gemini model {Model} is skipped for {Seconds}s", model, _options.CircuitBreakSeconds);
        }

        throw new HttpRequestException($"Gemini is unavailable ({string.Join(", ", errors)}). Please try again later.");
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout;

    /// <summary>
    /// Sends the request to one model, retrying transient errors with a short backoff.
    /// A hung model is reported as 408 so it falls back and trips the circuit breaker like an overloaded one.
    /// </summary>
    private async Task<(HttpStatusCode Status, string Body)> SendWithRetryAsync(string model, GeminiRequest request,
        int maxAttempts, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{model}:generateContent")
            {
                Content = JsonContent.Create(request, options: JsonOptions)
            };
            message.Headers.Add("x-goog-api-key", _options.ApiKey);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));

            HttpStatusCode status;
            string body;
            try
            {
                using var response = await http.SendAsync(message, attemptCts.Token);
                status = response.StatusCode;
                body = await response.Content.ReadAsStringAsync(attemptCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                status = HttpStatusCode.RequestTimeout;
                body = $"No response within {_options.RequestTimeoutSeconds}s";
            }

            if (!IsTransient(status) || attempt == maxAttempts)
                return (status, body);

            logger.LogWarning("Gemini model {Model} returned {Status}, retrying (attempt {Attempt}/{Max})",
                model, (int)status, attempt, maxAttempts);
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

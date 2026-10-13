using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Platform.Api.Models;

namespace Platform.Api.Services;

/// <summary>
/// Minimal Gemini REST API client (models/{model}:generateContent) without external SDKs.
/// </summary>
public class GeminiClient(HttpClient http, IOptions<GeminiOptions> options, ILogger<GeminiClient> logger) : ILlmClient
{
    private readonly GeminiOptions _options = options.Value;

    public async Task<string> GenerateAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Gemini:ApiKey is not configured.");

        var request = new GeminiRequest(
            Contents: history
                .Select(m => new GeminiContent(
                    // Gemini only supports the "user" and "model" roles
                    Role: m.Role == "assistant" ? "model" : "user",
                    Parts: [new GeminiPart(m.Text)]))
                .ToList(),
            SystemInstruction: new GeminiContent(null, [new GeminiPart(_options.SystemPrompt)]));

        // The free tier is frequently overloaded per model (503/429/500), and old models get
        // retired (404), so try the primary model first and then fall back to the others.
        var models = new[] { _options.Model }.Concat(_options.FallbackModels).Distinct().ToList();
        var errors = new List<string>();

        foreach (var model in models)
        {
            var (status, body) = await SendWithRetryAsync(model, request, ct);

            if (status == HttpStatusCode.OK)
            {
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

    private record GeminiRequest(List<GeminiContent> Contents, GeminiContent? SystemInstruction);
    private record GeminiContent(string? Role, List<GeminiPart>? Parts);
    private record GeminiPart(string? Text);
    private record GeminiResponse(List<GeminiCandidate>? Candidates);
    private record GeminiCandidate(GeminiContent? Content);
}

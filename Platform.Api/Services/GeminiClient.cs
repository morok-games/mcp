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

        using var message = new HttpRequestMessage(HttpMethod.Post, $"models/{_options.Model}:generateContent")
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        message.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var response = await http.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Gemini error {Status}: {Body}", (int)response.StatusCode, body);
            throw new HttpRequestException($"Gemini returned {(int)response.StatusCode}");
        }

        var result = JsonSerializer.Deserialize<GeminiResponse>(body, JsonOptions);
        var text = string.Concat(result?.Candidates?.FirstOrDefault()?.Content?.Parts?.Select(p => p.Text) ?? []);

        return string.IsNullOrWhiteSpace(text) ? "(empty response)" : text;
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

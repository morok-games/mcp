using Platform.Api.Models;

namespace Platform.Api.Services;

/// <summary>
/// Abstraction over the LLM provider so Gemini can be swapped (OpenAI, Claude, etc.)
/// without changing the controller.
/// </summary>
public interface ILlmClient
{
    /// <param name="systemPrompt">Instructions for this call; null uses the configured default prompt.</param>
    Task<string> GenerateAsync(IReadOnlyList<ChatMessage> history, string? systemPrompt = null, CancellationToken ct = default);

    /// <summary>
    /// Generates a response constrained to the JSON schema of <typeparamref name="T"/> and deserializes it.
    /// Used for data extraction, not for chat.
    /// </summary>
    Task<T> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

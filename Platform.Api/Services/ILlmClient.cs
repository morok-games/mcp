using Platform.Api.Models;

namespace Platform.Api.Services;

/// <summary>
/// Abstraction over the LLM provider so Gemini can be swapped (OpenAI, Claude, etc.)
/// without changing the controller.
/// </summary>
public interface ILlmClient
{
    Task<string> GenerateAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default);

    /// <summary>
    /// Generates a response constrained to the JSON schema of <typeparamref name="T"/> and deserializes it.
    /// Used for data extraction, not for chat.
    /// </summary>
    Task<T> GenerateStructuredAsync<T>(string systemPrompt, string userPrompt, CancellationToken ct = default);
}

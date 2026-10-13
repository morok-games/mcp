using Platform.Api.Models;

namespace Platform.Api.Services;

/// <summary>
/// Abstraction over the LLM provider so Gemini can be swapped (OpenAI, Claude, etc.)
/// without changing the controller.
/// </summary>
public interface ILlmClient
{
    Task<string> GenerateAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default);
}

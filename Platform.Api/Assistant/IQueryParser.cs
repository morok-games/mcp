using Platform.Api.Models;

namespace Platform.Api.Assistant;

/// <summary>
/// Step 1 of answering: turns the conversation into search parameters for the catalog.
/// Implementations are interchangeable (LLM structured output, function calling, rules, another provider).
/// </summary>
public interface IQueryParser
{
    /// <param name="history">Conversation ending with the user message to analyze.</param>
    Task<QueryUnderstanding> ParseAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default);
}

using Platform.Api.Catalog.Search;
using Platform.Api.Models;

namespace Platform.Api.Assistant;

/// <summary>
/// Step 3 of answering: writes the reply to the user from the conversation and catalog results.
/// Implementations are interchangeable (LLM, templates, another provider).
/// </summary>
public interface IAnswerComposer
{
    Task<string> ComposeAsync(AnswerContext context, CancellationToken ct = default);
}

/// <param name="History">Conversation ending with the user message being answered.</param>
/// <param name="Search">Catalog results; null when the message did not need a search (small talk, off-topic).</param>
/// <param name="Actions">Buttons that will be shown under the reply, so the text can refer to them.</param>
public sealed record AnswerContext(
    IReadOnlyList<ChatMessage> History,
    QueryUnderstanding Understanding,
    CatalogSearchResult? Search,
    IReadOnlyList<ChatAction> Actions);

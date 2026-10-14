namespace Platform.Api.Models;

/// <summary>A single chat message. Role: "user" or "assistant".</summary>
public record ChatMessage(string Role, string Text);

/// <summary>Request from the UI: the full chat history (the server is stateless).</summary>
public record ChatRequest(List<ChatMessage> Messages);

/// <param name="Reply">Assistant answer in Markdown.</param>
/// <param name="Actions">Buttons shown under the answer; built by our code from catalog data, never by the LLM.</param>
public record ChatResponse(string Reply, IReadOnlyList<ChatAction> Actions);

public enum ChatActionType
{
    /// <summary>Opens the business checkout page (Url).</summary>
    Checkout,
    /// <summary>Sends Text as the next user message.</summary>
    Reply
}

public record ChatAction(ChatActionType Type, string Label, string? Url = null, string? Text = null);

namespace Platform.Api.Models;

/// <summary>A single chat message. Role: "user" or "assistant".</summary>
public record ChatMessage(string Role, string Text);

/// <summary>Request from the UI: the full chat history (the server is stateless).</summary>
public record ChatRequest(List<ChatMessage> Messages);

public record ChatResponse(string Reply);

using System.Text;
using Platform.Api.Catalog;
using Platform.Api.Models;
using Platform.Api.Services;

namespace Platform.Api.Assistant;

/// <summary>
/// <see cref="IQueryParser"/> based on LLM structured output: the model fills <see cref="QueryUnderstanding"/>
/// by its JSON schema, then values are normalized deterministically.
/// </summary>
public class LlmQueryParser(ILlmClient llm) : IQueryParser
{
    // Enough context to resolve "it" / "I want to join" without sending the whole conversation
    private const int MaxHistoryMessages = 10;

    private const string SystemPrompt =
        """
        You analyze a customer's conversation with an AI shopping assistant and return search parameters
        for a catalog of businesses (training programs, classes, services, products).
        Rules:
        - Analyze the LAST user message. Use earlier messages only as context to resolve references
          like "it", "this program", "I want to join".
        - Set filters only when the user states or clearly implies them. Never guess.
        - Keywords must be in English (the catalog language) even if the user writes in another language.
        - Do not answer the user. Only return the parameters.
        """;

    public async Task<QueryUnderstanding> ParseAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        if (history.Count == 0 || history[^1].Role != "user")
            throw new ArgumentException("History must end with a user message.", nameof(history));

        var parsed = await llm.GenerateStructuredAsync<QueryUnderstanding>(SystemPrompt, BuildPrompt(history), ct);

        return Normalize(parsed);
    }

    private static string BuildPrompt(IReadOnlyList<ChatMessage> history)
    {
        var sb = new StringBuilder();
        var context = history.Take(history.Count - 1).TakeLast(MaxHistoryMessages - 1).ToList();

        if (context.Count > 0)
        {
            sb.AppendLine("Conversation so far:");
            foreach (var m in context)
                sb.AppendLine($"{(m.Role == "assistant" ? "Assistant" : "User")}: {m.Text}");
            sb.AppendLine();
        }

        sb.AppendLine("Last user message to analyze:");
        sb.AppendLine(history[^1].Text);

        return sb.ToString();
    }

    /// <summary>Cleans values the schema cannot enforce: casing, duplicates, formats, ranges.</summary>
    private static QueryUnderstanding Normalize(QueryUnderstanding q)
    {
        var currency = q.Currency is { Length: 3 } c && c.All(char.IsAsciiLetter) ? c.ToUpperInvariant() : null;
        var maxPrice = q.MaxPrice is > 0 ? q.MaxPrice : null;

        return new QueryUnderstanding
        {
            Intent = q.Intent,
            BrandMentions = Clean(q.BrandMentions),
            OfferingMentions = Clean(q.OfferingMentions),
            Keywords = Clean(q.Keywords.Select(k => k.ToLowerInvariant())),
            DeliveryMode = q.DeliveryMode,
            Kind = q.Kind,
            MaxPrice = maxPrice,
            // A price without a currency is assumed to be in USD, the catalog's default
            Currency = maxPrice is null ? null : currency ?? "USD",
            Duration = IsoDuration.IsValid(q.Duration) ? q.Duration : null,
            Level = string.IsNullOrWhiteSpace(q.Level) ? null : q.Level.Trim(),
            Language = q.Language is { Length: 2 } lang ? lang.ToLowerInvariant() : "en"
        };
    }

    private static List<string> Clean(IEnumerable<string> values) =>
        values.Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}

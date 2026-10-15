using System.Diagnostics;
using Platform.Api.Catalog.Checkout;
using Platform.Api.Catalog.Search;
using Platform.Api.Models;

namespace Platform.Api.Assistant;

/// <summary>
/// Answers a chat message in three steps: understand the question (LLM) -> search the catalog (SQL) ->
/// compose the reply (LLM). Action buttons, including checkout, are built here from catalog data.
/// </summary>
public class AssistantService(
    IQueryParser queryParser,
    ICatalogSearch catalogSearch,
    IAnswerComposer answerComposer,
    CheckoutTracker checkoutTracker,
    ILogger<AssistantService> logger)
{
    public async Task<ChatResponse> ReplyAsync(IReadOnlyList<ChatMessage> history, CancellationToken ct = default)
    {
        var total = Stopwatch.StartNew();

        var understanding = await queryParser.ParseAsync(history, ct);
        var parsedIn = total.Elapsed;

        var search = understanding.NeedsCatalogSearch()
            ? await catalogSearch.SearchAsync(understanding.ToSearchRequest(), ct)
            : null;
        var searchedIn = total.Elapsed - parsedIn;

        var actions = await BuildActionsAsync(understanding, search, ct);
        var reply = await answerComposer.ComposeAsync(new AnswerContext(history, understanding, search, actions), ct);

        logger.LogInformation(
            "Assistant reply: intent={Intent}, hits={Hits}, actions={Actions}; parse {Parse:F1}s, search {Search:F2}s, compose {Compose:F1}s",
            understanding.Intent, search?.Hits.Count ?? 0, actions.Count,
            parsedIn.TotalSeconds, searchedIn.TotalSeconds, (total.Elapsed - parsedIn - searchedIn).TotalSeconds);

        return new ChatResponse(reply, actions);
    }

    /// <summary>
    /// Enroll intent -> checkout button for the matched offering (only if enrollment is possible).
    /// Discovery/details -> quick reply that leads to enrollment.
    /// </summary>
    private async Task<List<ChatAction>> BuildActionsAsync(QueryUnderstanding understanding, CatalogSearchResult? search,
        CancellationToken ct)
    {
        var top = search?.Hits.FirstOrDefault()?.Offering;
        if (top is null)
            return [];

        if (understanding.Intent == QueryIntent.Enroll)
        {
            if (CheckoutPolicy.Decide(top).Price is not { } price)
                return [];

            // The button leads through our redirect so the transition to payment is recorded
            var link = await checkoutTracker.CreateLinkAsync(price, CheckoutChannel.WebChat, clientName: "Web chat", ct);
            return [new ChatAction(ChatActionType.Checkout, $"Enroll in {top.Name} — {CheckoutPolicy.FormatPrice(price)}", Url: link)];
        }

        return understanding.Intent is QueryIntent.FindOffering or QueryIntent.OfferingDetails or QueryIntent.Compare
               && CheckoutPolicy.CanEnroll(top)
            ? [new ChatAction(ChatActionType.Reply, $"I want to join {top.Name}", Text: $"I want to join the {top.Name}.")]
            : [];
    }
}

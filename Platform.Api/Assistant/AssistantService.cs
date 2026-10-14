using System.Diagnostics;
using System.Globalization;
using Platform.Api.Catalog.Entities;
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

        var actions = BuildActions(understanding, search);
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
    private static List<ChatAction> BuildActions(QueryUnderstanding understanding, CatalogSearchResult? search)
    {
        var top = search?.Hits.FirstOrDefault()?.Offering;
        if (top is null)
            return [];

        if (understanding.Intent == QueryIntent.Enroll)
        {
            var price = top.PricingOptions.FirstOrDefault(p => p.IsDefault && p.CheckoutUrl is not null)
                        ?? top.PricingOptions.FirstOrDefault(p => p.CheckoutUrl is not null);

            return price is not null && CanEnroll(top)
                ? [new ChatAction(ChatActionType.Checkout, $"Enroll in {top.Name} — {FormatPrice(price)}", Url: price.CheckoutUrl)]
                : [];
        }

        return understanding.Intent is QueryIntent.FindOffering or QueryIntent.OfferingDetails or QueryIntent.Compare
               && CanEnroll(top)
            ? [new ChatAction(ChatActionType.Reply, $"I want to join {top.Name}", Text: $"I want to join the {top.Name}.")]
            : [];
    }

    /// <summary>Enrollment is possible when the current run is open, or when no runs are published (rolling enrollment).</summary>
    private static bool CanEnroll(OfferingCard offering) =>
        offering.CurrentInstance is null || offering.CurrentInstance.EnrollmentStatus == EnrollmentStatus.Open;

    private static string FormatPrice(PriceCard price)
    {
        var amount = price.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        var symbol = price.Currency == "USD" ? "$" : price.Currency + " ";

        return price.BillingType switch
        {
            BillingType.Recurring when price.BillingInterval is { } interval => $"{symbol}{amount}/{interval.ToString().ToLowerInvariant()}",
            BillingType.Installments when price.InstallmentCount is { } count => $"{count} × {symbol}{amount}",
            _ => $"{symbol}{amount}"
        };
    }
}

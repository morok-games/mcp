using Platform.Api.Catalog.Search;

namespace Platform.Api.Assistant;

public static class QueryUnderstandingExtensions
{
    /// <summary>Greetings and off-topic questions are answered without touching the catalog.</summary>
    public static bool NeedsCatalogSearch(this QueryUnderstanding q) =>
        q.Intent is not (QueryIntent.General or QueryIntent.SmallTalk);

    public static CatalogSearchRequest ToSearchRequest(this QueryUnderstanding q) => new()
    {
        BrandMentions = q.BrandMentions,
        OfferingMentions = q.OfferingMentions,
        Keywords = q.Keywords,
        DeliveryMode = q.DeliveryMode,
        Kind = q.Kind,
        MaxPrice = q.MaxPrice,
        Currency = q.Currency,
        Duration = q.Duration,
        // Details and enrollment are about one specific offering; discovery shows a short list
        Limit = q.Intent is QueryIntent.OfferingDetails or QueryIntent.Enroll ? 1 : 5
    };
}

using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Platform.Api.Catalog.Checkout;
using Platform.Api.Catalog.Entities;
using Platform.Api.Catalog.Search;

namespace Platform.Api.Mcp;

/// <summary>
/// Catalog tools for external AI assistants (ChatGPT, Claude, Perplexity, ...) over MCP.
/// The client's own LLM understands the user and writes the answer; these tools only return verified
/// catalog data and the checkout link, reusing the same search and checkout rules as our chat.
/// </summary>
public class CatalogMcpTools(ICatalogSearch catalogSearch, ILogger<CatalogMcpTools> logger)
{
    private const int MaxResults = 10;

    [McpServerTool(Name = "search_offerings", Title = "Search programs and services",
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("""
        Search the catalog of businesses (online training programs, classes, services) and return matching offerings
        with verified price, duration, schedule, coach, level, what is included and enrollment state.
        Use it for any question about what a business offers, prices, schedules or how to join.
        Pass the user's request as 'query' and fill the structured filters you can infer.
        """)]
    public async Task<McpSearchResult> SearchOfferings(
        [Description("The user's request in natural language, e.g. 'online Double Axel program from Victory Skating'.")]
        string? query = null,
        [Description("Business/brand name if the user mentioned one, e.g. 'VSA' or 'Victory Skating'.")]
        string? brand = null,
        [Description("Only offerings delivered this way.")]
        DeliveryMode? deliveryMode = null,
        [Description("Program: multi-week course/club; Class: single session; Appointment: private lesson.")]
        OfferingKind? kind = null,
        [Description("Maximum price the user wants to pay, e.g. 350 for 'under $350'.")]
        decimal? maxPrice = null,
        [Description("ISO 4217 currency of maxPrice. Default USD.")]
        string? currency = null,
        [Description("Desired total length as ISO 8601 duration, e.g. P6M for six months.")]
        string? duration = null,
        [Description("Maximum number of offerings to return, 1-10. Default 5.")]
        int limit = 5,
        CancellationToken ct = default)
    {
        var result = await catalogSearch.SearchAsync(new CatalogSearchRequest
        {
            Query = query,
            BrandMentions = string.IsNullOrWhiteSpace(brand) ? [] : [brand.Trim()],
            DeliveryMode = deliveryMode,
            Kind = kind,
            MaxPrice = maxPrice is > 0 ? maxPrice : null,
            Currency = currency?.Trim().ToUpperInvariant(),
            Duration = Catalog.IsoDuration.IsValid(duration) ? duration : null,
            Limit = Math.Clamp(limit, 1, MaxResults)
        }, ct);

        logger.LogInformation("MCP search_offerings '{Query}' brand={Brand}: {Hits} hits, relaxed [{Relaxed}]",
            query, brand, result.Hits.Count, string.Join(", ", result.RelaxedFilters));

        return new McpSearchResult(
            result.Businesses,
            result.UnknownBrands,
            result.RelaxedFilters,
            result.Hits.Select(h => new McpOfferingHit(WithoutCheckoutLinks(h.Offering), h.MatchReasons, CheckoutPolicy.CanEnroll(h.Offering))).ToList(),
            BuildNote(result));
    }

    [McpServerTool(Name = "get_offering", Title = "Get offering details",
        ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false, UseStructuredContent = true)]
    [Description("Get full details of one offering (FAQ, schedule, pricing options, coach) by the slugs returned from search_offerings.")]
    public async Task<McpOfferingDetails> GetOffering(
        [Description("offering.business.slug from search_offerings, e.g. 'victory-skating'.")]
        string businessSlug,
        [Description("offering.slug from search_offerings, e.g. 'doubleaxel'.")]
        string offeringSlug,
        CancellationToken ct = default)
    {
        var offering = await catalogSearch.GetOfferingAsync(businessSlug, offeringSlug, ct)
                       ?? throw new McpException($"Offering '{businessSlug}/{offeringSlug}' was not found. Use search_offerings to find valid slugs.");

        var decision = CheckoutPolicy.Decide(offering);
        return new McpOfferingDetails(WithoutCheckoutLinks(offering), decision.CanCheckout, decision.Reason);
    }

    [McpServerTool(Name = "create_checkout", Title = "Start enrollment and payment",
        ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("""
        Start enrollment when the user wants to join or buy an offering. Returns the business's secure checkout link
        for the chosen pricing option. Payment is completed by the user on that page; nothing is charged by this call.
        Give the user the link as a clickable link together with the price.
        """)]
    public async Task<McpCheckout> CreateCheckout(
        [Description("offering.business.slug from search_offerings.")]
        string businessSlug,
        [Description("offering.slug from search_offerings.")]
        string offeringSlug,
        [Description("Id of a specific pricing option from offering.pricingOptions; omit to use the default one.")]
        Guid? pricingOptionId = null,
        CancellationToken ct = default)
    {
        var offering = await catalogSearch.GetOfferingAsync(businessSlug, offeringSlug, ct)
                       ?? throw new McpException($"Offering '{businessSlug}/{offeringSlug}' was not found. Use search_offerings to find valid slugs.");

        var decision = CheckoutPolicy.Decide(offering, pricingOptionId);
        if (decision.Price is not { } price)
            throw new McpException(decision.Reason!);

        logger.LogInformation("MCP create_checkout {Business}/{Offering}: {Price} -> {Url}",
            businessSlug, offeringSlug, CheckoutPolicy.FormatPrice(price), price.CheckoutUrl);

        return new McpCheckout(
            CheckoutUrl: price.CheckoutUrl!,
            BusinessName: offering.Business.Name,
            OfferingName: offering.Name,
            PricingOption: price.Name,
            Price: CheckoutPolicy.FormatPrice(price),
            Amount: price.Amount,
            Currency: price.Currency,
            BillingType: price.BillingType,
            Instructions: $"Open the checkout link to pay {CheckoutPolicy.FormatPrice(price)} on {offering.Business.Name}'s secure payment page. " +
                          "Enrollment is confirmed by the business after payment.");
    }

    /// <summary>Checkout links are handed out only by create_checkout, so enrollment is an explicit step.</summary>
    private static OfferingCard WithoutCheckoutLinks(OfferingCard offering) => offering with
    {
        PricingOptions = offering.PricingOptions.Select(p => p with { CheckoutUrl = null }).ToList()
    };

    private static string? BuildNote(CatalogSearchResult result)
    {
        if (result.UnknownBrands.Count > 0 && result.Hits.Count == 0)
            return $"No data for: {string.Join(", ", result.UnknownBrands)}. Do not present other businesses' offerings as theirs.";
        if (result.Hits.Count == 0)
            return "Nothing found. Ask the user to clarify; do not invent offerings.";
        if (result.RelaxedFilters.Count > 0)
            return $"Nothing matched exactly; these filters were relaxed: {string.Join(", ", result.RelaxedFilters)}. Tell the user how the results differ.";
        return null;
    }
}

public sealed record McpSearchResult(
    IReadOnlyList<BusinessMatch> MatchedBusinesses,
    IReadOnlyList<string> UnknownBrands,
    IReadOnlyList<SearchFilter> RelaxedFilters,
    IReadOnlyList<McpOfferingHit> Offerings,
    [property: Description("Guidance for presenting these results honestly.")] string? Note);

public sealed record McpOfferingHit(OfferingCard Offering, IReadOnlyList<string> MatchReasons, bool CanEnroll);

public sealed record McpOfferingDetails(
    OfferingCard Offering,
    bool CanEnroll,
    [property: Description("Why enrollment is not possible, if CanEnroll is false.")] string? EnrollmentNote);

public sealed record McpCheckout(
    string CheckoutUrl,
    string BusinessName,
    string OfferingName,
    string PricingOption,
    string Price,
    decimal Amount,
    string Currency,
    BillingType BillingType,
    string Instructions);

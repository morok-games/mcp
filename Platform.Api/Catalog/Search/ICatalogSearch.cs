using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Search;

/// <summary>
/// Finds non-archived offerings for structured search criteria. Implementations are interchangeable
/// (PostgreSQL full-text, vector search, external engine).
/// </summary>
public interface ICatalogSearch
{
    Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken ct = default);
}

public sealed record CatalogSearchRequest
{
    /// <summary>Brand names as written by the user; resolved against Business name/aliases/slug.</summary>
    public IReadOnlyList<string> BrandMentions { get; init; } = [];

    /// <summary>Offering names as written by the user; searched as phrases.</summary>
    public IReadOnlyList<string> OfferingMentions { get; init; } = [];

    public IReadOnlyList<string> Keywords { get; init; } = [];

    public DeliveryMode? DeliveryMode { get; init; }
    public OfferingKind? Kind { get; init; }
    public decimal? MaxPrice { get; init; }
    public string? Currency { get; init; }

    /// <summary>ISO 8601 duration, e.g. "P6M".</summary>
    public string? Duration { get; init; }

    public int Limit { get; init; } = 5;
}

/// <summary>Optional filters in the order they are relaxed when nothing matches.</summary>
public enum SearchFilter
{
    Duration,
    Kind,
    DeliveryMode,
    MaxPrice,
    Text
}

/// <param name="Businesses">Brand mentions resolved to businesses.</param>
/// <param name="UnknownBrands">Brand mentions that match no business: the assistant must not invent offerings for them.</param>
/// <param name="RelaxedFilters">Filters dropped to get results; the assistant should say the exact request had no match.</param>
public sealed record CatalogSearchResult(
    IReadOnlyList<BusinessMatch> Businesses,
    IReadOnlyList<string> UnknownBrands,
    IReadOnlyList<SearchFilter> RelaxedFilters,
    IReadOnlyList<OfferingSearchHit> Hits);

public sealed record BusinessMatch(string Mention, string BusinessSlug, string BusinessName);

/// <param name="MatchReasons">Why the offering matched, e.g. "'double axel' found in name", "6-Month Package 299 USD ≤ 350 USD".</param>
public sealed record OfferingSearchHit(OfferingCard Offering, double Score, IReadOnlyList<string> MatchReasons);

/// <summary>Everything the assistant needs to describe and sell an offering.</summary>
public sealed record OfferingCard
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> Aliases { get; init; }
    public required OfferingKind Kind { get; init; }
    public required DeliveryMode DeliveryMode { get; init; }
    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public required IReadOnlyList<string> Tags { get; init; }
    public string? Duration { get; init; }
    public int? SessionCount { get; init; }
    public required Audience Audience { get; init; }
    public required IReadOnlyList<string> Inclusions { get; init; }
    public required IReadOnlyList<OfferingAttribute> Attributes { get; init; }
    public required IReadOnlyList<FaqItem> Faq { get; init; }
    public string? Url { get; init; }
    public required BusinessCard Business { get; init; }
    public required IReadOnlyList<StaffCard> Staff { get; init; }
    public required IReadOnlyList<PriceCard> PricingOptions { get; init; }

    /// <summary>The run a customer can join now (or the most relevant one if none is open).</summary>
    public InstanceCard? CurrentInstance { get; init; }

    /// <summary>When the data was last checked against the source.</summary>
    public DateTimeOffset? VerifiedAt { get; init; }
}

public sealed record BusinessCard(string Slug, string Name, IReadOnlyList<string> Aliases, string? WebsiteUrl, string TimeZone);

public sealed record StaffCard(string Name, string? Role, string? Bio, string? Credentials);

public sealed record PriceCard(
    Guid Id,
    string Name,
    decimal Amount,
    string Currency,
    BillingType BillingType,
    BillingInterval? BillingInterval,
    int? InstallmentCount,
    bool IsDefault,
    string? CheckoutUrl);

public sealed record InstanceCard(
    string Name,
    DateOnly? StartDate,
    DateOnly? EndDate,
    EnrollmentStatus EnrollmentStatus,
    DateTimeOffset? EnrollmentDeadline,
    int? SeatsLeft,
    IReadOnlyList<ScheduleRule> Schedule);

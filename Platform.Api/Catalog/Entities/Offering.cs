namespace Platform.Api.Catalog.Entities;

/// <summary>
/// Anything a business sells: a program, class, appointment, membership or product.
/// Universal, filterable data lives in typed fields; vertical-specific data goes to <see cref="Attributes"/>.
/// </summary>
public class Offering : CatalogEntity
{
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>Alternative product names, e.g. "2A Club".</summary>
    public List<string> Aliases { get; set; } = [];

    public OfferingKind Kind { get; set; }

    public OfferingStatus Status { get; set; } = OfferingStatus.Draft;

    /// <summary>One or two sentences for search results.</summary>
    public string? ShortDescription { get; set; }

    public string? Description { get; set; }

    public string? Category { get; set; }

    public List<string> Tags { get; set; } = [];

    public DeliveryMode DeliveryMode { get; set; }

    /// <summary>ISO 8601 duration: "P6M" for a 6-month program, "PT45M" for a 45-minute appointment.</summary>
    public string? Duration { get; set; }

    /// <summary>Total number of sessions/classes; null for products.</summary>
    public int? SessionCount { get; set; }

    public Audience Audience { get; set; } = new();

    /// <summary>What the customer gets, as separate items AI can list.</summary>
    public List<string> Inclusions { get; set; } = [];

    public List<OfferingAttribute> Attributes { get; set; } = [];

    public List<FaqItem> Faq { get; set; } = [];

    /// <summary>Canonical product page.</summary>
    public string? Url { get; set; }

    public string? ImageUrl { get; set; }

    public List<Staff> Staff { get; set; } = [];

    public List<PricingOption> PricingOptions { get; set; } = [];

    public List<OfferingInstance> Instances { get; set; } = [];
}

/// <summary>Who the offering is for. Level is free text because every vertical has its own scale.</summary>
public class Audience
{
    public string? LevelText { get; set; }

    public string? Prerequisites { get; set; }

    public int? AgeMin { get; set; }

    public int? AgeMax { get; set; }
}

/// <summary>Vertical-specific property, e.g. Key="discipline", Label="Discipline", Value="Figure skating".</summary>
public class OfferingAttribute
{
    public required string Key { get; set; }

    public required string Label { get; set; }

    public required string Value { get; set; }

    public string? Unit { get; set; }
}

public class FaqItem
{
    public required string Question { get; set; }

    public required string Answer { get; set; }
}

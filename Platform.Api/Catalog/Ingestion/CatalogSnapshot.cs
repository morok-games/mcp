using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Ingestion;

// Normalized, platform-agnostic import model produced by every adapter.
// Each record carries an ExternalId: a stable key in the source platform used to match
// existing rows on re-import (Shopify product id, Tilda page id, canonical URL, etc.).

public sealed record CatalogSnapshot(
    BusinessSnapshot Business,
    IReadOnlyList<StaffSnapshot> Staff,
    IReadOnlyList<OfferingSnapshot> Offerings);

public sealed record BusinessSnapshot
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? Description { get; init; }
    public string? WebsiteUrl { get; init; }
    public string DefaultCurrency { get; init; } = "USD";
    public string TimeZone { get; init; } = "UTC";
    public string Language { get; init; } = "en";
}

public sealed record StaffSnapshot
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public string? Role { get; init; }
    public string? Bio { get; init; }
    public string? Credentials { get; init; }
    public string? ImageUrl { get; init; }
}

public sealed record OfferingSnapshot
{
    public required string ExternalId { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public required OfferingKind Kind { get; init; }

    /// <summary>Status for newly created offerings; null means Draft (requires review before AI uses it).</summary>
    public OfferingStatus? Status { get; init; }

    public string? ShortDescription { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public required DeliveryMode DeliveryMode { get; init; }

    /// <summary>ISO 8601 duration, e.g. "P6M".</summary>
    public string? Duration { get; init; }

    public int? SessionCount { get; init; }
    public Audience Audience { get; init; } = new();
    public IReadOnlyList<string> Inclusions { get; init; } = [];
    public IReadOnlyList<OfferingAttribute> Attributes { get; init; } = [];
    public IReadOnlyList<FaqItem> Faq { get; init; } = [];
    public string? Url { get; init; }
    public string? ImageUrl { get; init; }

    /// <summary>References to <see cref="StaffSnapshot.ExternalId"/> in the same snapshot.</summary>
    public IReadOnlyList<string> StaffExternalIds { get; init; } = [];

    public IReadOnlyList<PricingOptionSnapshot> PricingOptions { get; init; } = [];
    public IReadOnlyList<OfferingInstanceSnapshot> Instances { get; init; } = [];
}

public sealed record PricingOptionSnapshot
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public required decimal Amount { get; init; }
    public required string Currency { get; init; }
    public required BillingType BillingType { get; init; }
    public BillingInterval? BillingInterval { get; init; }
    public int? InstallmentCount { get; init; }
    public bool IsDefault { get; init; }
    public string? CheckoutUrl { get; init; }
}

public sealed record OfferingInstanceSnapshot
{
    public required string ExternalId { get; init; }
    public required string Name { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public IReadOnlyList<ScheduleRule> Schedule { get; init; } = [];
    public EnrollmentStatus EnrollmentStatus { get; init; } = EnrollmentStatus.Open;
    public DateTimeOffset? EnrollmentDeadline { get; init; }
    public int? Capacity { get; init; }
    public int? SeatsLeft { get; init; }
}

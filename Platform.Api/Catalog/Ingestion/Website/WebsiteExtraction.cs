using System.ComponentModel;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Ingestion.Website;

// Output contract of the LLM extraction step. It is sent to the model as a JSON schema
// (descriptions included), so property names and descriptions are part of the prompt.
// Values are validated against the page by WebsiteSnapshotMapper before they reach the snapshot.

public sealed class WebsiteExtraction
{
    public required ExtractedBusiness Business { get; init; }

    [Description("People who deliver the offerings (coaches, instructors). Only people named on the page.")]
    public List<ExtractedStaff> Staff { get; init; } = [];

    [Description("Products/programs sold on this page. Usually one per page.")]
    public List<ExtractedOffering> Offerings { get; init; } = [];

    [Description("Ambiguities or contradictions noticed on the page, e.g. conflicting billing terms or a date without a year.")]
    public List<string> Warnings { get; init; } = [];
}

public sealed class ExtractedBusiness
{
    [Description("Brand name as presented to customers.")]
    public required string Name { get; init; }

    [Description("Other names of the same brand found on the page: abbreviations, alternative spellings, brand names in domains or emails.")]
    public List<string> Aliases { get; init; } = [];

    [Description("One or two sentences about the business, only from page content.")]
    public string? Description { get; init; }

    [Description("IANA time zone the business schedules in, e.g. America/Los_Angeles. Null if unknown.")]
    public string? TimeZone { get; init; }

    [Description("ISO 4217 currency of the prices, e.g. USD.")]
    public string? Currency { get; init; }
}

public sealed class ExtractedStaff
{
    [Description("Name exactly as written on the page, e.g. 'Coach Marta' -> 'Marta'.")]
    public required string Name { get; init; }

    public string? Role { get; init; }

    public string? Bio { get; init; }

    [Description("Education, certifications, clubs and teams trained with.")]
    public string? Credentials { get; init; }
}

public sealed class ExtractedOffering
{
    public required string Name { get; init; }

    [Description("Other names used for this offering on the page. Not level labels, prices or generic words.")]
    public List<string> Aliases { get; init; } = [];

    public required OfferingKind Kind { get; init; }

    [Description("One sentence suitable for a search result.")]
    public string? ShortDescription { get; init; }

    [Description("Full description of the program content, a few sentences.")]
    public string? Description { get; init; }

    [Description("Broad category, e.g. 'Figure Skating'.")]
    public string? Category { get; init; }

    [Description("Lowercase search keywords: skills, goals, format.")]
    public List<string> Tags { get; init; } = [];

    public required DeliveryMode DeliveryMode { get; init; }

    [Description("Total length as ISO 8601 duration, e.g. P6M for six months.")]
    public string? Duration { get; init; }

    [Description("Total number of sessions/classes included.")]
    public int? SessionCount { get; init; }

    [Description("Level label as written, e.g. 'Level 3'.")]
    public string? LevelText { get; init; }

    [Description("What the customer must already be able to do.")]
    public string? Prerequisites { get; init; }

    public int? AgeMin { get; init; }

    public int? AgeMax { get; init; }

    [Description("Each thing the customer gets, as a separate item.")]
    public List<string> Inclusions { get; init; } = [];

    [Description("Other relevant facts as key/value pairs, e.g. training environment, required equipment, session length.")]
    public List<ExtractedAttribute> Attributes { get; init; } = [];

    [Description("Questions a buyer would ask, answered strictly from the page.")]
    public List<ExtractedFaq> Faq { get; init; } = [];

    [Description("Names of staff (from the Staff list) who deliver this offering.")]
    public List<string> StaffNames { get; init; } = [];

    public List<ExtractedPricingOption> PricingOptions { get; init; } = [];

    [Description("Concrete runs/cohorts with schedule and enrollment state.")]
    public List<ExtractedInstance> Instances { get; init; } = [];
}

public sealed class ExtractedAttribute
{
    [Description("snake_case key.")]
    public required string Key { get; init; }

    public required string Label { get; init; }

    public required string Value { get; init; }

    public string? Unit { get; init; }
}

public sealed class ExtractedFaq
{
    public required string Question { get; init; }

    public required string Answer { get; init; }
}

public sealed class ExtractedPricingOption
{
    public required string Name { get; init; }

    [Description("The price the customer actually pays per charge now. Not a crossed-out 'was' price, not a derived per-class price.")]
    public required decimal Amount { get; init; }

    public required string Currency { get; init; }

    [Description("OneTime if the whole period is paid at once.")]
    public required BillingType BillingType { get; init; }

    public BillingInterval? BillingInterval { get; init; }

    public int? InstallmentCount { get; init; }

    [Description("Original price before discount, if shown.")]
    public decimal? CompareAtAmount { get; init; }

    [Description("Checkout URL for this option, chosen only from the provided list of checkout links.")]
    public string? CheckoutUrl { get; init; }
}

public sealed class ExtractedInstance
{
    [Description("e.g. 'October 2026 cohort'.")]
    public required string Name { get; init; }

    [Description("yyyy-MM-dd. If the page gives a date without a year, use the next upcoming occurrence after today.")]
    public string? StartDate { get; init; }

    [Description("yyyy-MM-dd.")]
    public string? EndDate { get; init; }

    public required EnrollmentStatus EnrollmentStatus { get; init; }

    [Description("Weekly slots. If the same slot is listed in several time zones, return it once in the business time zone.")]
    public List<ExtractedScheduleRule> Schedule { get; init; } = [];
}

public sealed class ExtractedScheduleRule
{
    public List<DayOfWeek> DaysOfWeek { get; init; } = [];

    [Description("24h local start time, HH:mm.")]
    public required string StartTime { get; init; }

    public required int DurationMinutes { get; init; }

    [Description("IANA time zone, e.g. America/Los_Angeles for PDT/PST.")]
    public required string TimeZone { get; init; }
}

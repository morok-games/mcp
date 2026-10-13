namespace Platform.Api.Catalog.Entities;

public class Business : CatalogEntity
{
    public required string Slug { get; set; }

    public required string Name { get; set; }

    /// <summary>Alternative brand names AI should recognize, e.g. "VSA" for "Victory Skating".</summary>
    public List<string> Aliases { get; set; } = [];

    public string? Description { get; set; }

    public string? WebsiteUrl { get; set; }

    /// <summary>ISO 4217 code, e.g. "USD".</summary>
    public string DefaultCurrency { get; set; } = "USD";

    /// <summary>IANA time zone, e.g. "America/New_York".</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>ISO 639-1 code of the catalog content.</summary>
    public string Language { get; set; } = "en";

    public List<Offering> Offerings { get; set; } = [];

    public List<Staff> Staff { get; set; } = [];
}

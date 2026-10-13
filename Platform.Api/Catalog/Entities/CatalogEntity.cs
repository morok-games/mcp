namespace Platform.Api.Catalog.Entities;

/// <summary>
/// Common fields for every catalog record: identity, origin in an external platform and freshness.
/// </summary>
public abstract class CatalogEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public SourcePlatform SourcePlatform { get; set; } = SourcePlatform.Manual;

    /// <summary>Id of the record in the source platform (Shopify product id, Mindbody class id, etc.).</summary>
    public string? ExternalId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>When the data was last checked against the source, so stale prices are not served to AI.</summary>
    public DateTimeOffset? VerifiedAt { get; set; }
}

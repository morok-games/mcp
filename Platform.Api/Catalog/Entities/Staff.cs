namespace Platform.Api.Catalog.Entities;

/// <summary>Person delivering the offering: coach, instructor, stylist, etc.</summary>
public class Staff : CatalogEntity
{
    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Free text role as the business calls it, e.g. "Head Coach".</summary>
    public string? Role { get; set; }

    public string? Bio { get; set; }

    public string? Credentials { get; set; }

    public string? ImageUrl { get; set; }

    public List<Offering> Offerings { get; set; } = [];
}

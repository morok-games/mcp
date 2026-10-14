using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

public class OfferingConfiguration : IEntityTypeConfiguration<Offering>
{
    public void Configure(EntityTypeBuilder<Offering> builder)
    {
        builder.ConfigureCatalogEntity();

        builder.Property(o => o.Slug).HasMaxLength(100);
        builder.Property(o => o.Name).HasMaxLength(300);
        builder.Property(o => o.Category).HasMaxLength(200);
        builder.Property(o => o.Duration).HasMaxLength(32);

        // Flattened into audience_* columns so age/level can be filtered directly
        builder.ComplexProperty(o => o.Audience);

        builder.ComplexCollection(o => o.Attributes, a => a.ToJson());
        builder.ComplexCollection(o => o.Faq, f => f.ToJson());

        builder.HasOne(o => o.Business)
            .WithMany(b => b.Offerings)
            .HasForeignKey(o => o.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(o => o.Staff)
            .WithMany(s => s.Offerings)
            .UsingEntity<Dictionary<string, object>>(
                "offering_staff",
                right => right.HasOne<Staff>().WithMany().HasForeignKey("StaffId"),
                left => left.HasOne<Offering>().WithMany().HasForeignKey("OfferingId"));

        builder.HasIndex(o => new { o.BusinessId, o.Slug }).IsUnique();
        builder.HasIndex(o => new { o.Status, o.Kind });
        builder.HasIndex(o => o.Aliases).HasMethod("gin");
        builder.HasIndex(o => o.Tags).HasMethod("gin");

        builder.HasIndex(o => new { o.BusinessId, o.SourcePlatform, o.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");

        // Full-text search vector maintained by PostgreSQL. A shadow property keeps the Npgsql type out of the entity.
        builder.Property<NpgsqlTsVector>(SearchVectorProperty)
            .HasComputedColumnSql(SearchVectorSql, stored: true);
        builder.HasIndex(SearchVectorProperty).HasMethod("gin");
    }

    public const string SearchVectorProperty = "SearchVector";

    // Weights drive ranking: A = name/aliases, B = tags/category, C = summary/level, D = long text.
    // catalog_array_to_text is an IMMUTABLE wrapper over array_to_string (created in the migration),
    // required because generated columns only accept immutable functions.
    private const string SearchVectorSql =
        """
        setweight(to_tsvector('english'::regconfig, coalesce(name, '')), 'A') ||
        setweight(to_tsvector('english'::regconfig, catalog_array_to_text(aliases)), 'A') ||
        setweight(to_tsvector('english'::regconfig, catalog_array_to_text(tags)), 'B') ||
        setweight(to_tsvector('english'::regconfig, coalesce(category, '')), 'B') ||
        setweight(to_tsvector('english'::regconfig, coalesce(short_description, '')), 'C') ||
        setweight(to_tsvector('english'::regconfig, coalesce(audience_level_text, '') || ' ' || coalesce(audience_prerequisites, '')), 'C') ||
        setweight(to_tsvector('english'::regconfig, coalesce(description, '')), 'D') ||
        setweight(to_tsvector('english'::regconfig, catalog_array_to_text(inclusions)), 'D')
        """;
}

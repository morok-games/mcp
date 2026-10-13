using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

public class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> builder)
    {
        builder.ConfigureCatalogEntity();

        builder.Property(b => b.Slug).HasMaxLength(100);
        builder.Property(b => b.Name).HasMaxLength(200);
        builder.Property(b => b.DefaultCurrency).HasMaxLength(3).IsFixedLength();
        builder.Property(b => b.TimeZone).HasMaxLength(64);
        builder.Property(b => b.Language).HasMaxLength(8);

        builder.HasIndex(b => b.Slug).IsUnique();
        // GIN index for alias lookups like: 'VSA' = ANY(aliases)
        builder.HasIndex(b => b.Aliases).HasMethod("gin");

        builder.HasIndex(b => new { b.SourcePlatform, b.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");
    }
}

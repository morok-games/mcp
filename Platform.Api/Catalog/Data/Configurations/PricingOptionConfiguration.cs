using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

public class PricingOptionConfiguration : IEntityTypeConfiguration<PricingOption>
{
    public void Configure(EntityTypeBuilder<PricingOption> builder)
    {
        builder.ConfigureCatalogEntity();

        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Amount).HasPrecision(12, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength();

        builder.HasOne(p => p.Offering)
            .WithMany(o => o.PricingOptions)
            .HasForeignKey(p => p.OfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.OfferingId, p.SourcePlatform, p.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");
    }
}

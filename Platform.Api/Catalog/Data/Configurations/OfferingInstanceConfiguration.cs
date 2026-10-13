using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

public class OfferingInstanceConfiguration : IEntityTypeConfiguration<OfferingInstance>
{
    public void Configure(EntityTypeBuilder<OfferingInstance> builder)
    {
        builder.ConfigureCatalogEntity();

        builder.Property(i => i.Name).HasMaxLength(200);

        builder.ComplexCollection(i => i.Schedule, s => s.ToJson());

        builder.HasOne(i => i.Offering)
            .WithMany(o => o.Instances)
            .HasForeignKey(i => i.OfferingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(i => new { i.OfferingId, i.EnrollmentStatus });

        builder.HasIndex(i => new { i.OfferingId, i.SourcePlatform, i.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");
    }
}

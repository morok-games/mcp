using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

public class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.ConfigureCatalogEntity();

        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.Role).HasMaxLength(100);

        builder.HasOne(s => s.Business)
            .WithMany(b => b.Staff)
            .HasForeignKey(s => s.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.BusinessId, s.SourcePlatform, s.ExternalId })
            .IsUnique()
            .HasFilter("external_id IS NOT NULL");
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

public class BusinessSourceConfiguration : IEntityTypeConfiguration<BusinessSource>
{
    public void Configure(EntityTypeBuilder<BusinessSource> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Url).HasMaxLength(2048);

        builder.HasOne(s => s.Business)
            .WithMany(b => b.Sources)
            .HasForeignKey(s => s.BusinessId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

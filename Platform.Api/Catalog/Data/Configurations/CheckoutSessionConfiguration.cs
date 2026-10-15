using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Checkout;

namespace Platform.Api.Catalog.Data.Configurations;

public class CheckoutSessionConfiguration : IEntityTypeConfiguration<CheckoutSession>
{
    public void Configure(EntityTypeBuilder<CheckoutSession> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Amount).HasPrecision(12, 2);
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(s => s.Channel).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.ClientName).HasMaxLength(256);
        builder.Property(s => s.DestinationUrl).HasMaxLength(2048);

        // Sessions are analytics of a business: removed together with it
        builder.HasOne(s => s.Business).WithMany().HasForeignKey(s => s.BusinessId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Offering).WithMany().HasForeignKey(s => s.OfferingId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.PricingOption).WithMany().HasForeignKey(s => s.PricingOptionId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.BusinessId, s.CreatedAt });
    }
}

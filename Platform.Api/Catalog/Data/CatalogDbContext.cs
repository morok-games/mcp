using Microsoft.EntityFrameworkCore;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Offering> Offerings => Set<Offering>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<PricingOption> PricingOptions => Set<PricingOption>();
    public DbSet<OfferingInstance> OfferingInstances => Set<OfferingInstance>();
    public DbSet<BusinessSource> BusinessSources => Set<BusinessSource>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store enums as readable strings: safer for reordering and easier to query by hand
        configurationBuilder.Properties<SourcePlatform>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<OfferingKind>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<OfferingStatus>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<DeliveryMode>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<BillingType>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<BillingInterval>().HaveConversion<string>().HaveMaxLength(32);
        configurationBuilder.Properties<EnrollmentStatus>().HaveConversion<string>().HaveMaxLength(32);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        SetTimestamps();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SetTimestamps();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void SetTimestamps()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<CatalogEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}

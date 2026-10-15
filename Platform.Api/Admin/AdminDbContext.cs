using Microsoft.EntityFrameworkCore;

namespace Platform.Api.Admin;

/// <summary>Admin accounts. Kept separate from the catalog so business data and credentials do not mix.</summary>
public class AdminDbContext(DbContextOptions<AdminDbContext> options) : DbContext(options)
{
    public DbSet<AdminUser> Admins => Set<AdminUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminUser>(admin =>
        {
            admin.ToTable("admin_users");
            admin.HasKey(a => a.Id);
            admin.Property(a => a.Id).ValueGeneratedNever();
            admin.Property(a => a.Username).HasMaxLength(64);
            admin.HasIndex(a => a.Username).IsUnique();
        });
    }
}

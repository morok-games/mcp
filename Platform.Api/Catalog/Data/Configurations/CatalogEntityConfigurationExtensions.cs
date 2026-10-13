using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Data.Configurations;

internal static class CatalogEntityConfigurationExtensions
{
    /// <summary>Shared mapping for <see cref="CatalogEntity"/> fields.</summary>
    public static void ConfigureCatalogEntity<T>(this EntityTypeBuilder<T> builder) where T : CatalogEntity
    {
        builder.HasKey(e => e.Id);
        // Ids are generated client-side as UUID v7 (time-ordered, index friendly)
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ExternalId).HasMaxLength(200);
    }
}

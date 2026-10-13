using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Ingestion;

/// <summary>Picks the adapter registered for a source platform.</summary>
public class CatalogAdapterResolver(IEnumerable<ICatalogSourceAdapter> adapters)
{
    private readonly Dictionary<SourcePlatform, ICatalogSourceAdapter> _adapters =
        adapters.ToDictionary(a => a.Platform);

    public IReadOnlyCollection<SourcePlatform> SupportedPlatforms => _adapters.Keys;

    public ICatalogSourceAdapter Resolve(SourcePlatform platform) =>
        _adapters.TryGetValue(platform, out var adapter)
            ? adapter
            : throw new NotSupportedException($"No catalog adapter is registered for '{platform}'.");
}

namespace Platform.Api.Catalog.Ingestion;

/// <summary>Entry point of the pipeline: source -> adapter -> snapshot -> importer -> database.</summary>
public class CatalogIngestionService(
    CatalogAdapterResolver resolver,
    CatalogImporter importer,
    ILogger<CatalogIngestionService> logger)
{
    /// <summary>Reads and normalizes the source without saving: used for review (dry run).</summary>
    public async Task<CatalogSnapshot> FetchAsync(CatalogSource source, CancellationToken ct = default)
    {
        var adapter = resolver.Resolve(source.Platform);

        logger.LogInformation("Fetching catalog for {Business} from {Platform}", source.BusinessSlug, source.Platform);
        var snapshot = await adapter.FetchAsync(source, ct);

        foreach (var warning in snapshot.Warnings)
            logger.LogWarning("Catalog {Business}: {Warning}", source.BusinessSlug, warning);

        return snapshot;
    }

    public async Task<(CatalogSnapshot Snapshot, CatalogImportResult Result)> IngestAsync(CatalogSource source, CancellationToken ct = default)
    {
        var snapshot = await FetchAsync(source, ct);

        var result = await importer.ImportAsync(source, snapshot, ct);
        logger.LogInformation("Catalog import for {Business} finished: {Result}", source.BusinessSlug, result);

        return (snapshot, result);
    }
}

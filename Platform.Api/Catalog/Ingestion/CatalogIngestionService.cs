namespace Platform.Api.Catalog.Ingestion;

/// <summary>Entry point of the pipeline: source -> adapter -> snapshot -> importer -> database.</summary>
public class CatalogIngestionService(
    CatalogAdapterResolver resolver,
    CatalogImporter importer,
    ILogger<CatalogIngestionService> logger)
{
    public async Task<CatalogImportResult> IngestAsync(CatalogSource source, CancellationToken ct = default)
    {
        var adapter = resolver.Resolve(source.Platform);

        logger.LogInformation("Fetching catalog for {Business} from {Platform}", source.BusinessSlug, source.Platform);
        var snapshot = await adapter.FetchAsync(source, ct);

        var result = await importer.ImportAsync(source, snapshot, ct);
        logger.LogInformation("Catalog import for {Business} finished: {Result}", source.BusinessSlug, result);

        return result;
    }
}

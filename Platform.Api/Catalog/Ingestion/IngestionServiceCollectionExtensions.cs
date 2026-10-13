namespace Platform.Api.Catalog.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogIngestion(this IServiceCollection services)
    {
        services.AddScoped<CatalogImporter>();
        services.AddScoped<CatalogAdapterResolver>();
        services.AddScoped<CatalogIngestionService>();

        // Register one adapter per SourcePlatform here, e.g.:
        // services.AddScoped<ICatalogSourceAdapter, TildaCatalogAdapter>();

        return services;
    }
}

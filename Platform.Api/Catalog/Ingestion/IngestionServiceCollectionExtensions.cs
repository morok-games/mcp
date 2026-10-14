using Platform.Api.Catalog.Ingestion.Tilda;
using Platform.Api.Catalog.Ingestion.Website;

namespace Platform.Api.Catalog.Ingestion;

public static class IngestionServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogIngestion(this IServiceCollection services)
    {
        services.AddScoped<CatalogImporter>();
        services.AddScoped<CatalogAdapterResolver>();
        services.AddScoped<CatalogIngestionService>();

        // One adapter per SourcePlatform
        services.AddWebsiteAdapter<WebsiteCatalogAdapter>(); // SourcePlatform.Custom: any website
        services.AddWebsiteAdapter<TildaCatalogAdapter>();

        return services;
    }

    private static void AddWebsiteAdapter<TAdapter>(this IServiceCollection services)
        where TAdapter : class, ICatalogSourceAdapter
    {
        services.AddHttpClient<TAdapter>(http =>
        {
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; AICommerceCatalogBot/1.0)");
        });
        services.AddScoped<ICatalogSourceAdapter>(sp => sp.GetRequiredService<TAdapter>());
    }
}

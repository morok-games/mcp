using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Ingestion;

/// <summary>
/// Reads a business catalog from one external platform and converts it to the platform-agnostic
/// <see cref="CatalogSnapshot"/>. Adapters never touch the database: persistence is shared and
/// handled by <see cref="CatalogImporter"/>, so a new platform only needs a new adapter.
/// </summary>
public interface ICatalogSourceAdapter
{
    SourcePlatform Platform { get; }

    Task<CatalogSnapshot> FetchAsync(CatalogSource source, CancellationToken ct = default);
}

/// <summary>
/// Where to read the catalog from.
/// </summary>
/// <param name="Platform">Selects the adapter.</param>
/// <param name="BusinessSlug">Business in our system the data belongs to.</param>
/// <param name="Url">Entry point for website-based sources (Tilda, WordPress, custom sites).</param>
/// <param name="Settings">Platform-specific settings: shop domain, site id, API token reference, etc.</param>
/// <param name="ExtractionInstructions">Admin hints for LLM-based adapters about this particular source.</param>
public sealed record CatalogSource(
    SourcePlatform Platform,
    string BusinessSlug,
    Uri? Url = null,
    IReadOnlyDictionary<string, string>? Settings = null,
    string? ExtractionInstructions = null);

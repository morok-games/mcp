using AngleSharp.Dom;
using Platform.Api.Catalog.Entities;
using Platform.Api.Catalog.Ingestion.Website;
using Platform.Api.Services;

namespace Platform.Api.Catalog.Ingestion.Tilda;

/// <summary>
/// Tilda pages: content lives in #allrecords as a sequence of "records" (div.t-rec), header and footer
/// are separate record collections (#t-header, #t-footer). Page and project ids are stable data attributes,
/// unlike record ids (rec123...) which change when the owner edits the page.
/// </summary>
public class TildaCatalogAdapter(HttpClient http, ILlmClient llm, ILogger<TildaCatalogAdapter> logger)
    : WebsiteCatalogAdapter(http, llm, logger)
{
    // Tilda block types that never carry product data: menus (446, 978), cookie banner (972)
    private static readonly HashSet<string> NoiseRecordTypes = ["446", "978", "972"];

    public override SourcePlatform Platform => SourcePlatform.Tilda;

    protected override void RemoveNoise(IDocument document)
    {
        base.RemoveNoise(document);

        foreach (var element in document.QuerySelectorAll("#t-header, #t-footer, .t-menu, .t-popup").ToList())
            element.Remove();

        foreach (var record in document.QuerySelectorAll(".t-rec[data-record-type]").ToList())
        {
            if (NoiseRecordTypes.Contains(record.GetAttribute("data-record-type")!))
                record.Remove();
        }
    }

    protected override IEnumerable<WebPageBlock> ExtractBlocks(IDocument document)
    {
        var records = AllRecords(document)?.QuerySelectorAll(".t-rec");
        if (records is null || records.Length == 0)
            return base.ExtractBlocks(document); // not a standard Tilda layout

        // Record ids are not stable, so blocks are named by position and Tilda block type
        return records.Select((r, i) => new WebPageBlock(
            $"block{i + 1} (tilda type {r.GetAttribute("data-record-type") ?? "?"})",
            HtmlText.Extract(r)));
    }

    protected override string GetPageExternalId(IDocument document, Uri url, string? canonicalUrl) =>
        AllRecords(document)?.GetAttribute("data-tilda-page-id")
        ?? base.GetPageExternalId(document, url, canonicalUrl);

    protected override string GetSiteExternalId(IDocument document, Uri url) =>
        AllRecords(document)?.GetAttribute("data-tilda-project-id")
        ?? base.GetSiteExternalId(document, url);

    private static IElement? AllRecords(IDocument document) => document.GetElementById("allrecords");
}

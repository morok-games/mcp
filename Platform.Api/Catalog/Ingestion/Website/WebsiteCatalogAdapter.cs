using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Platform.Api.Catalog.Entities;
using Platform.Api.Services;

namespace Platform.Api.Catalog.Ingestion.Website;

/// <summary>
/// Hybrid adapter for websites without a product API:
/// 1) deterministic layer parses HTML into <see cref="WebPage"/> (blocks, checkout links, prices);
/// 2) LLM layer turns page text into <see cref="WebsiteExtraction"/> using a strict JSON schema;
/// 3) <see cref="WebsiteSnapshotMapper"/> cross-checks the LLM output against the page and builds the snapshot.
/// Site builders (Tilda, Wix, ...) derive from it and override only the HTML-specific hooks.
/// </summary>
public partial class WebsiteCatalogAdapter : ICatalogSourceAdapter
{
    private const int MaxPromptTextLength = 40_000;

    private readonly HttpClient _http;
    private readonly ILlmClient _llm;
    protected readonly ILogger Logger;

    public WebsiteCatalogAdapter(HttpClient http, ILlmClient llm, ILogger<WebsiteCatalogAdapter> logger)
        : this(http, llm, (ILogger)logger)
    {
    }

    protected WebsiteCatalogAdapter(HttpClient http, ILlmClient llm, ILogger logger)
    {
        _http = http;
        _llm = llm;
        Logger = logger;
    }

    public virtual SourcePlatform Platform => SourcePlatform.Custom;

    /// <summary>Hosts whose links are treated as checkout/payment actions.</summary>
    protected virtual IReadOnlyList<string> CheckoutHosts { get; } =
    [
        "buy.stripe.com", "checkout.stripe.com", "donate.stripe.com",
        "paypal.com", "paypal.me", "square.link", "checkout.square.site",
        "gumroad.com", "checkout.shopify.com", "pay.hotmart.com", "app.thrivecart.com"
    ];

    public async Task<CatalogSnapshot> FetchAsync(CatalogSource source, CancellationToken ct = default)
    {
        var url = source.Url ?? throw new ArgumentException($"{Platform} source requires a Url.", nameof(source));

        var html = await _http.GetStringAsync(url, ct);
        var document = await new HtmlParser().ParseDocumentAsync(html, ct);

        var page = ParsePage(document, url);
        Logger.LogInformation("Parsed {Url}: {Blocks} blocks, {Checkout} checkout links, amounts [{Amounts}], crossed out [{CrossedOut}]",
            url, page.Blocks.Count, page.CheckoutLinks.Count, string.Join(", ", page.Amounts), string.Join(", ", page.CrossedOutAmounts));

        var extraction = await _llm.GenerateStructuredAsync<WebsiteExtraction>(
            ExtractionSystemPrompt, BuildExtractionPrompt(page, source.ExtractionInstructions), ct);

        return WebsiteSnapshotMapper.Map(page, extraction);
    }

    /// <summary>Deterministic layer: everything that can be read from HTML without interpretation.</summary>
    protected virtual WebPage ParsePage(IDocument document, Uri url)
    {
        // Read metadata before noise removal: it lives in <head>
        var canonical = document.QuerySelector("link[rel=canonical]")?.GetAttribute("href");
        var title = document.Title?.Trim();
        var metaDescription = Meta(document, "description") ?? Meta(document, "og:description");
        var image = Meta(document, "og:image");
        var language = document.DocumentElement.GetAttribute("lang");

        RemoveNoise(document);

        var blocks = ExtractBlocks(document).Where(b => b.Text.Length > 0).ToList();
        var text = string.Join("\n", blocks.Select(b => b.Text));

        var links = ExtractLinks(document, url);

        // Split amounts into current and crossed-out ones using the ~~...~~ markers from HtmlText
        var struckSegments = StruckTextRegex().Matches(text).Select(m => m.Value);
        var currentAmounts = ExtractAmounts(StruckTextRegex().Replace(text, " "));
        var crossedOutAmounts = ExtractAmounts(string.Join(" ", struckSegments));
        crossedOutAmounts.ExceptWith(currentAmounts);

        return new WebPage
        {
            Url = url,
            ExternalId = GetPageExternalId(document, url, canonical),
            SiteExternalId = GetSiteExternalId(document, url),
            Title = title,
            MetaDescription = metaDescription,
            CanonicalUrl = canonical,
            ImageUrl = image,
            Language = string.IsNullOrWhiteSpace(language) ? null : language.Split('-')[0].ToLowerInvariant(),
            Blocks = blocks,
            Links = links,
            CheckoutLinks = links.Where(l => IsCheckoutUrl(l.Url)).DistinctBy(l => l.Url).ToList(),
            Amounts = currentAmounts,
            CrossedOutAmounts = crossedOutAmounts,
            Emails = EmailRegex().Matches(text).Select(m => m.Value.ToLowerInvariant()).Distinct().ToList()
        };
    }

    /// <summary>Removes elements that are not page content. Override to drop builder-specific widgets.</summary>
    protected virtual void RemoveNoise(IDocument document)
    {
        foreach (var element in document.QuerySelectorAll(
                     "script, style, noscript, template, svg, iframe, nav, [id*=cookie i], [class*=cookie i]").ToList())
            element.Remove();
    }

    /// <summary>Splits the page into content blocks. Default: top-level children of main/body.</summary>
    protected virtual IEnumerable<WebPageBlock> ExtractBlocks(IDocument document)
    {
        var root = document.QuerySelector("main") ?? document.Body;
        if (root is null)
            yield break;

        var index = 0;
        foreach (var child in root.Children)
            yield return new WebPageBlock($"block{++index}", HtmlText.Extract(child));
    }

    protected virtual string GetPageExternalId(IDocument document, Uri url, string? canonicalUrl) =>
        canonicalUrl ?? url.GetLeftPart(UriPartial.Path);

    protected virtual string GetSiteExternalId(IDocument document, Uri url) => url.Host;

    protected virtual bool IsCheckoutUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        CheckoutHosts.Any(h => uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
                               uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    private static List<WebLink> ExtractLinks(IDocument document, Uri baseUrl) =>
        document.QuerySelectorAll("a[href]")
            .Select(a => (Href: a.GetAttribute("href")!, Text: HtmlText.Extract(a).Replace('\n', ' ')))
            .Where(l => !l.Href.StartsWith('#') && !l.Href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            .Select(l => Uri.TryCreate(baseUrl, l.Href, out var abs) ? new WebLink(abs.ToString(), l.Text) : null)
            .OfType<WebLink>()
            .ToList();

    private static HashSet<decimal> ExtractAmounts(string text) =>
        MoneyRegex().Matches(text)
            .Select(m => decimal.TryParse(m.Groups["amount"].Value.Replace(",", ""), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var amount) ? amount : (decimal?)null)
            .OfType<decimal>()
            .ToHashSet();

    private static string? Meta(IDocument document, string name)
    {
        var value = document.QuerySelector($"meta[name='{name}'], meta[property='{name}']")?.GetAttribute("content");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private const string ExtractionSystemPrompt =
        """
        You extract structured product catalog data from a business web page for an AI shopping assistant.
        Rules:
        - Use only facts stated on the page. Never invent or guess values; use null or empty lists when not stated.
        - Text wrapped in ~~ ~~ is crossed out on the page (an old price), never the current price.
        - Prices: return the price the customer actually pays now. A crossed-out or "regular" price goes to CompareAtAmount.
        - Each checkout link charges one price; do not invent extra pricing options for the same link.
          A per-class price derived from the package price is not a separate pricing option.
        - CheckoutUrl must be copied exactly from the provided checkout links list, or be null.
        - If one schedule is shown in several time zones, return it once, in the business's own time zone.
        - Ignore navigation, cookie notices, testimonials and links to other products.
        - Do not lose information: every informational section (how it works, requirements, enrollment steps, policies)
          must end up in a field, typically as an FAQ item.
        - Report contradictions (e.g. "monthly subscription" vs a one-time 6-month price) in Warnings.
        """;

    private static string BuildExtractionPrompt(WebPage page, string? instructions)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Today: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"Page URL: {page.CanonicalUrl ?? page.Url.ToString()}");
        sb.AppendLine($"Title: {page.Title}");
        if (page.MetaDescription is not null) sb.AppendLine($"Meta description: {page.MetaDescription}");
        if (page.Emails.Count > 0) sb.AppendLine($"Emails: {string.Join(", ", page.Emails)}");

        sb.AppendLine().AppendLine("Checkout links (button text -> URL):");
        foreach (var link in page.CheckoutLinks)
            sb.AppendLine($"- {link.Text} -> {link.Url}");
        if (page.CheckoutLinks.Count == 0)
            sb.AppendLine("- none");

        sb.AppendLine().AppendLine("Page content by blocks:");
        var budget = MaxPromptTextLength;
        foreach (var block in page.Blocks)
        {
            if (budget <= 0) break;
            var text = block.Text.Length > budget ? block.Text[..budget] : block.Text;
            sb.AppendLine($"### {block.Id}").AppendLine(text).AppendLine();
            budget -= text.Length;
        }

        // Placed last so these site-specific hints are the freshest context for the model
        if (!string.IsNullOrWhiteSpace(instructions))
        {
            sb.AppendLine("Additional instructions from the catalog admin for this business (follow them; they override the general rules):");
            sb.AppendLine(instructions.Trim());
        }

        return sb.ToString();
    }

    [GeneratedRegex(@"(?:\$|USD\s?|US\$)\s?(?<amount>\d{1,3}(?:,\d{3})+(?:\.\d{1,2})?|\d+(?:\.\d{1,2})?)")]
    private static partial Regex MoneyRegex();

    [GeneratedRegex(@"~~.*?~~", RegexOptions.Singleline)]
    private static partial Regex StruckTextRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();
}

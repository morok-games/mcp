using System.Text;
using AngleSharp.Dom;

namespace Platform.Api.Catalog.Ingestion.Website;

/// <summary>
/// Deterministic facts read from a page without any LLM. Used both as LLM input and as ground truth
/// to validate what the LLM returns (prices, checkout links, names must exist on the page).
/// </summary>
public sealed record WebPage
{
    public required Uri Url { get; init; }

    /// <summary>Stable id of the page in the source (Tilda page id, canonical URL, etc.).</summary>
    public required string ExternalId { get; init; }

    /// <summary>Stable id of the whole site/business in the source (Tilda project id, host, etc.).</summary>
    public required string SiteExternalId { get; init; }

    public string? Title { get; init; }
    public string? MetaDescription { get; init; }
    public string? CanonicalUrl { get; init; }
    public string? ImageUrl { get; init; }
    public string? Language { get; init; }

    /// <summary>Content blocks in page order, already cleaned from menus, cookie banners and scripts.</summary>
    public required IReadOnlyList<WebPageBlock> Blocks { get; init; }

    public required IReadOnlyList<WebLink> Links { get; init; }

    /// <summary>Links to payment providers (Stripe, PayPal, ...): the only allowed checkout URLs.</summary>
    public required IReadOnlyList<WebLink> CheckoutLinks { get; init; }

    /// <summary>Money amounts shown as current (not crossed out) somewhere on the page, e.g. 299, 6.</summary>
    public required IReadOnlySet<decimal> Amounts { get; init; }

    /// <summary>Money amounts that appear only crossed out (old prices), e.g. 699.</summary>
    public required IReadOnlySet<decimal> CrossedOutAmounts { get; init; }

    public required IReadOnlyList<string> Emails { get; init; }

    public string Text => string.Join("\n\n", Blocks.Select(b => b.Text));
}

public sealed record WebPageBlock(string Id, string Text);

public sealed record WebLink(string Url, string Text);

public static class HtmlText
{
    private static readonly HashSet<string> BlockTags =
    [
        "P", "DIV", "BR", "LI", "UL", "OL", "TR", "TABLE", "SECTION", "ARTICLE", "HEADER", "FOOTER",
        "H1", "H2", "H3", "H4", "H5", "H6", "BLOCKQUOTE", "FIGCAPTION", "BUTTON", "A"
    ];

    public const string StrikeMarker = "~~";

    private static bool IsStrikethrough(IElement element) =>
        element.TagName is "S" or "DEL" or "STRIKE" ||
        (element.GetAttribute("style")?.Contains("line-through", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Visible text with line breaks at block boundaries (AngleSharp's TextContent glues words
    /// from adjacent elements together, e.g. "$699$6/class").
    /// </summary>
    public static string Extract(INode node)
    {
        var sb = new StringBuilder();
        Append(node, sb);

        var lines = sb.ToString()
            .Split('\n')
            .Select(l => string.Join(' ', l.Split([' ', '\t', '\r', ' '], StringSplitOptions.RemoveEmptyEntries)))
            .Where(l => l.Length > 0);

        return string.Join('\n', lines);
    }

    private static void Append(INode node, StringBuilder sb)
    {
        switch (node)
        {
            case IText text:
                sb.Append(text.Data);
                break;
            case IElement element:
                var isBlock = BlockTags.Contains(element.TagName);
                // Keep crossed-out text visible as ~~text~~: it is how sites show old prices
                var isStruck = IsStrikethrough(element);
                if (isBlock) sb.Append('\n');
                if (isStruck) sb.Append(StrikeMarker);
                foreach (var child in element.ChildNodes)
                    Append(child, sb);
                if (isStruck) sb.Append(StrikeMarker);
                if (isBlock) sb.Append('\n');
                break;
            default:
                foreach (var child in node.ChildNodes)
                    Append(child, sb);
                break;
        }
    }
}

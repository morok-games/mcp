using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using NpgsqlTypes;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Data.Configurations;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Search;

/// <summary>
/// Catalog search on PostgreSQL:
/// - brands are resolved deterministically against Business name/aliases/slug;
/// - structured criteria (delivery mode, kind, price, duration) are SQL filters;
/// - keywords and offering names go to full-text search over the weighted offerings.search_vector;
/// - if nothing matches, optional filters are relaxed one by one and reported, so the answer stays honest.
/// </summary>
public class PostgresCatalogSearch(CatalogDbContext db) : ICatalogSearch
{
    private const string TextSearchConfig = "english";

    // websearch_to_tsquery ignores these anyway; dropping them keeps match reasons free of noise like "'an' found"
    private static readonly HashSet<string> StopWords =
    [
        "a", "an", "and", "any", "are", "as", "at", "be", "can", "do", "does", "for", "from", "have", "has", "i", "in",
        "is", "it", "me", "my", "of", "on", "or", "the", "there", "to", "want", "what", "with", "you", "your", "anything"
    ];

    private static readonly SearchFilter[] RelaxationOrder =
        [SearchFilter.Duration, SearchFilter.Kind, SearchFilter.DeliveryMode, SearchFilter.MaxPrice, SearchFilter.Text];

    public async Task<CatalogSearchResult> SearchAsync(CatalogSearchRequest request, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.Query))
            request = await ExpandFreeTextAsync(request, ct);

        var (businesses, businessIds, unknownBrands) = await ResolveBrandsAsync(request.BrandMentions, ct);

        // The user asked about a brand we do not have: return nothing rather than other brands' offerings
        if (request.BrandMentions.Count > 0 && businesses.Count == 0)
            return new CatalogSearchResult(businesses, unknownBrands, [], []);

        var tsQuery = BuildTsQuery(request);
        var applicable = RelaxationOrder.Where(f => IsRequested(f, request, tsQuery)).ToList();
        var relaxed = new List<SearchFilter>();

        while (true)
        {
            var active = applicable.Except(relaxed).ToHashSet();
            var ranked = await FindRankedIdsAsync(request, businessIds, tsQuery, active, ct);

            if (ranked.Count > 0 || relaxed.Count == applicable.Count)
            {
                var hits = await LoadHitsAsync(ranked, request, businessIds, businesses, active, ct);
                // Relaxed filters only matter when relaxing actually produced results
                return new CatalogSearchResult(businesses, unknownBrands, hits.Count > 0 ? relaxed : [], hits);
            }

            relaxed.Add(applicable.First(f => !relaxed.Contains(f)));
        }
    }

    /// <summary>
    /// Turns a free-text query into structured criteria: business names/aliases found in it (as whole words)
    /// become brand mentions, the remaining words become keywords.
    /// </summary>
    private async Task<CatalogSearchRequest> ExpandFreeTextAsync(CatalogSearchRequest request, CancellationToken ct)
    {
        var text = request.Query!;

        // Fine for a catalog of hundreds of businesses; a larger one would match aliases in SQL
        var names = await db.Businesses.AsNoTracking()
            .Select(b => new { b.Name, b.Aliases })
            .ToListAsync(ct);

        var detectedBrands = new List<string>();
        foreach (var name in names.SelectMany(b => b.Aliases.Prepend(b.Name)).OrderByDescending(n => n.Length))
        {
            var pattern = $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}])";
            if (!Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
                continue;

            detectedBrands.Add(name);
            // Brand words should not drive full-text matching
            text = Regex.Replace(text, pattern, " ", RegexOptions.IgnoreCase);
        }

        var words = Regex.Matches(text, @"[\p{L}\p{N}]+")
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w.Length >= 2 && !StopWords.Contains(w));

        return request with
        {
            Query = null,
            BrandMentions = request.BrandMentions.Concat(detectedBrands).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Keywords = request.Keywords.Concat(words).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public async Task<OfferingCard?> GetOfferingAsync(string businessSlug, string offeringSlug, CancellationToken ct = default)
    {
        var offering = await db.Offerings.AsNoTracking()
            .Where(o => o.Business.Slug == businessSlug && o.Slug == offeringSlug && o.Status != OfferingStatus.Archived)
            .Include(o => o.Business)
            .Include(o => o.Staff)
            .Include(o => o.PricingOptions.Where(p => p.IsActive))
            .Include(o => o.Instances)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);

        return offering is null ? null : ToCard(offering);
    }

    private async Task<(List<BusinessMatch> Matches, List<Guid> Ids, List<string> Unknown)> ResolveBrandsAsync(
        IReadOnlyList<string> mentions, CancellationToken ct)
    {
        if (mentions.Count == 0)
            return ([], [], []);

        var lower = mentions.Select(m => m.ToLowerInvariant()).ToList();
        var slugs = mentions.Select(Slug.From).ToList();

        var candidates = await db.Businesses.AsNoTracking()
            .Where(b => lower.Contains(b.Name.ToLower()) || slugs.Contains(b.Slug) ||
                        b.Aliases.Any(a => lower.Contains(a.ToLower())))
            .Select(b => new { b.Id, b.Slug, b.Name, b.Aliases })
            .ToListAsync(ct);

        var matches = new List<BusinessMatch>();
        var ids = new List<Guid>();
        var unknown = new List<string>();

        foreach (var mention in mentions)
        {
            var business = candidates.FirstOrDefault(b =>
                b.Name.Equals(mention, StringComparison.OrdinalIgnoreCase) ||
                b.Slug == Slug.From(mention) ||
                b.Aliases.Any(a => a.Equals(mention, StringComparison.OrdinalIgnoreCase)));

            if (business is null)
            {
                unknown.Add(mention);
                continue;
            }

            matches.Add(new BusinessMatch(mention, business.Slug, business.Name));
            ids.Add(business.Id);
        }

        return (matches, ids.Distinct().ToList(), unknown);
    }

    private static bool IsRequested(SearchFilter filter, CatalogSearchRequest r, string? tsQuery) => filter switch
    {
        SearchFilter.Duration => r.Duration is not null,
        SearchFilter.Kind => r.Kind is not null,
        SearchFilter.DeliveryMode => r.DeliveryMode is not null,
        SearchFilter.MaxPrice => r.MaxPrice is not null,
        SearchFilter.Text => tsQuery is not null,
        _ => false
    };

    /// <summary>
    /// Keywords and offering names as a websearch_to_tsquery expression: phrases in quotes, joined with "or",
    /// e.g. "double axel" or 2axel or "double axel club".
    /// </summary>
    private static string? BuildTsQuery(CatalogSearchRequest r)
    {
        var terms = r.Keywords.Concat(r.OfferingMentions)
            .Select(t => new string(t.Where(c => char.IsLetterOrDigit(c) || c == ' ' || c == '\'').ToArray()).Trim())
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(t => t.Contains(' ') ? $"\"{t}\"" : t)
            .ToList();

        return terms.Count == 0 ? null : string.Join(" or ", terms);
    }

    private async Task<List<(Guid Id, float Rank)>> FindRankedIdsAsync(CatalogSearchRequest r, List<Guid> businessIds,
        string? tsQuery, HashSet<SearchFilter> active, CancellationToken ct)
    {
        // Draft offerings are searchable for now (no review step in the admin panel yet); archived ones are gone from the source
        var query = db.Offerings.AsNoTracking().Where(o => o.Status != OfferingStatus.Archived);

        if (businessIds.Count > 0)
            query = query.Where(o => businessIds.Contains(o.BusinessId));

        if (active.Contains(SearchFilter.DeliveryMode))
        {
            // Hybrid offerings satisfy both online and in-person requests
            var mode = r.DeliveryMode!.Value;
            query = query.Where(o => o.DeliveryMode == mode || o.DeliveryMode == DeliveryMode.Hybrid);
        }

        if (active.Contains(SearchFilter.Kind))
            query = query.Where(o => o.Kind == r.Kind!.Value);

        if (active.Contains(SearchFilter.Duration))
            query = query.Where(o => o.Duration == r.Duration);

        if (active.Contains(SearchFilter.MaxPrice))
        {
            var max = r.MaxPrice!.Value;
            var currency = r.Currency ?? "USD";
            query = query.Where(o => o.PricingOptions.Any(p => p.IsActive && p.Currency == currency && p.Amount <= max));
        }

        if (!active.Contains(SearchFilter.Text))
        {
            var ids = await query.OrderBy(o => o.Name).Select(o => o.Id).Take(r.Limit).ToListAsync(ct);
            return ids.Select(id => (id, 0f)).ToList();
        }

        var ranked = await query
            .Where(o => EF.Property<NpgsqlTsVector>(o, OfferingConfiguration.SearchVectorProperty)
                .Matches(EF.Functions.WebSearchToTsQuery(TextSearchConfig, tsQuery!)))
            .Select(o => new
            {
                o.Id,
                Rank = EF.Property<NpgsqlTsVector>(o, OfferingConfiguration.SearchVectorProperty)
                    .Rank(EF.Functions.WebSearchToTsQuery(TextSearchConfig, tsQuery!))
            })
            .OrderByDescending(x => x.Rank)
            .Take(r.Limit)
            .ToListAsync(ct);

        return ranked.Select(x => (x.Id, x.Rank)).ToList();
    }

    private async Task<List<OfferingSearchHit>> LoadHitsAsync(List<(Guid Id, float Rank)> ranked, CatalogSearchRequest r,
        List<Guid> businessIds, List<BusinessMatch> businesses, HashSet<SearchFilter> active, CancellationToken ct)
    {
        if (ranked.Count == 0)
            return [];

        var ids = ranked.Select(x => x.Id).ToList();
        var offerings = await db.Offerings.AsNoTracking()
            .Where(o => ids.Contains(o.Id))
            .Include(o => o.Business)
            .Include(o => o.Staff)
            .Include(o => o.PricingOptions.Where(p => p.IsActive))
            .Include(o => o.Instances)
            .AsSplitQuery()
            .ToListAsync(ct);

        var byId = offerings.ToDictionary(o => o.Id);

        return ranked
            .Where(x => byId.ContainsKey(x.Id))
            .Select(x => new OfferingSearchHit(
                ToCard(byId[x.Id]),
                Math.Round(x.Rank, 4),
                ExplainMatch(byId[x.Id], r, businessIds, businesses, active)))
            .ToList();
    }

    private static List<string> ExplainMatch(Offering o, CatalogSearchRequest r, List<Guid> businessIds,
        List<BusinessMatch> businesses, HashSet<SearchFilter> active)
    {
        var reasons = new List<string>();

        if (businessIds.Contains(o.BusinessId))
        {
            var mentions = businesses.Where(b => b.BusinessSlug == o.Business.Slug).Select(b => $"'{b.Mention}'");
            reasons.Add($"brand {string.Join(", ", mentions)} is {o.Business.Name}");
        }

        if (active.Contains(SearchFilter.Text))
        {
            var fields = new (string Name, IEnumerable<string?> Values)[]
            {
                ("name", [o.Name]), ("aliases", o.Aliases), ("tags", o.Tags), ("category", [o.Category]),
                ("summary", [o.ShortDescription]), ("description", [o.Description]), ("inclusions", o.Inclusions)
            };

            foreach (var term in r.Keywords.Concat(r.OfferingMentions).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var field = fields.FirstOrDefault(f =>
                    f.Values.Any(v => v?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
                if (field.Name is not null)
                    reasons.Add($"'{term}' found in {field.Name}");
            }
        }

        if (active.Contains(SearchFilter.MaxPrice))
        {
            var currency = r.Currency ?? "USD";
            var price = o.PricingOptions
                .Where(p => p.Currency == currency && p.Amount <= r.MaxPrice)
                .OrderBy(p => p.Amount)
                .FirstOrDefault();
            if (price is not null)
                reasons.Add($"{price.Name}: {Money(price.Amount)} {price.Currency} ≤ {Money(r.MaxPrice!.Value)} {currency}");
        }

        if (active.Contains(SearchFilter.DeliveryMode))
            reasons.Add($"delivery: {o.DeliveryMode}");
        if (active.Contains(SearchFilter.Kind))
            reasons.Add($"kind: {o.Kind}");
        if (active.Contains(SearchFilter.Duration))
            reasons.Add($"duration: {o.Duration}");

        return reasons;
    }

    private static OfferingCard ToCard(Offering o) => new()
    {
        Slug = o.Slug,
        Name = o.Name,
        Aliases = o.Aliases,
        Kind = o.Kind,
        DeliveryMode = o.DeliveryMode,
        ShortDescription = o.ShortDescription,
        Description = o.Description,
        Category = o.Category,
        Tags = o.Tags,
        Duration = o.Duration,
        SessionCount = o.SessionCount,
        Audience = o.Audience,
        Inclusions = o.Inclusions,
        Attributes = o.Attributes,
        Faq = o.Faq,
        Url = o.Url,
        Business = new BusinessCard(o.Business.Slug, o.Business.Name, o.Business.Aliases, o.Business.WebsiteUrl, o.Business.TimeZone),
        Staff = o.Staff.Select(s => new StaffCard(s.Name, s.Role, s.Bio, s.Credentials)).ToList(),
        PricingOptions = o.PricingOptions
            .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Amount)
            .Select(p => new PriceCard(p.Id, p.Name, p.Amount, p.Currency, p.BillingType, p.BillingInterval,
                p.InstallmentCount, p.IsDefault, p.CheckoutUrl))
            .ToList(),
        CurrentInstance = PickCurrentInstance(o.Instances) is { } i
            ? new InstanceCard(i.Name, i.StartDate, i.EndDate, i.EnrollmentStatus, i.EnrollmentDeadline, i.SeatsLeft, i.Schedule)
            : null,
        VerifiedAt = o.VerifiedAt
    };

    /// <summary>Open runs first, then waitlist/coming soon; within a status the nearest upcoming start, then the latest one.</summary>
    private static OfferingInstance? PickCurrentInstance(List<OfferingInstance> instances)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return instances
            .OrderBy(i => i.EnrollmentStatus switch
            {
                EnrollmentStatus.Open => 0,
                EnrollmentStatus.Waitlist => 1,
                EnrollmentStatus.ComingSoon => 2,
                _ => 3
            })
            .ThenBy(i => i.StartDate is { } start && start >= today ? 0 : 1)
            .ThenBy(i => i.StartDate is { } start && start >= today ? start.DayNumber : -(i.StartDate?.DayNumber ?? 0))
            .FirstOrDefault();
    }

    private static string Money(decimal amount) => amount.ToString("0.##", CultureInfo.InvariantCulture);
}

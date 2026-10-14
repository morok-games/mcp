using System.Globalization;
using System.Text.RegularExpressions;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Ingestion.Website;

/// <summary>
/// Converts LLM output into a <see cref="CatalogSnapshot"/>, keeping only values confirmed by the page:
/// prices must appear in the page text, checkout URLs must be real links from the page, staff names must
/// be mentioned on the page, time zones/dates/durations must be valid. Everything dropped becomes a warning.
/// </summary>
public static partial class WebsiteSnapshotMapper
{
    public static CatalogSnapshot Map(WebPage page, WebsiteExtraction extraction)
    {
        var warnings = new List<string>(extraction.Warnings.Select(w => "LLM: " + w));
        var pageText = page.Text;

        var currency = ValidCurrency(extraction.Business.Currency) ?? "USD";
        var timeZone = ValidTimeZone(extraction.Business.TimeZone, warnings, "Business") ?? "UTC";

        var business = new BusinessSnapshot
        {
            ExternalId = page.SiteExternalId,
            Name = extraction.Business.Name,
            Aliases = CleanList(extraction.Business.Aliases, exclude: extraction.Business.Name),
            Description = extraction.Business.Description,
            WebsiteUrl = page.Url.GetLeftPart(UriPartial.Authority),
            DefaultCurrency = currency,
            TimeZone = timeZone,
            Language = page.Language ?? "en"
        };

        var staff = new List<StaffSnapshot>();
        foreach (var s in extraction.Staff)
        {
            if (!Mentioned(pageText, s.Name))
            {
                warnings.Add($"Staff '{s.Name}' is not mentioned on the page and was dropped.");
                continue;
            }

            staff.Add(new StaffSnapshot
            {
                ExternalId = Slug.From(s.Name),
                Name = s.Name,
                Role = s.Role,
                Bio = s.Bio,
                Credentials = s.Credentials
            });
        }

        staff = staff.DistinctBy(s => s.ExternalId).ToList();
        var staffIds = staff.Select(s => s.ExternalId).ToHashSet();

        var offerings = extraction.Offerings
            .Select((o, index) => MapOffering(o, index, extraction.Offerings.Count, page, currency, staffIds, warnings))
            .ToList();

        if (offerings.Count == 0)
            warnings.Add("No offerings were extracted from the page.");

        return new CatalogSnapshot(business, staff, offerings, warnings);
    }

    private static OfferingSnapshot MapOffering(ExtractedOffering o, int index, int total, WebPage page,
        string defaultCurrency, HashSet<string> staffIds, List<string> warnings)
    {
        // One offering per page is the common case: key it by the page id and slug it by the URL path,
        // both stable across content edits. Several offerings on one page are keyed by name.
        var single = total == 1;
        var pathSlug = Slug.From(page.Url.AbsolutePath);
        var externalId = single ? page.ExternalId : $"{page.ExternalId}#{Slug.From(o.Name)}";
        var slug = single && pathSlug.Length > 0 ? pathSlug : Slug.From(o.Name);
        var scope = $"Offering '{o.Name}'";

        var attributes = o.Attributes
            .Select(a => new OfferingAttribute { Key = a.Key, Label = a.Label, Value = a.Value, Unit = a.Unit })
            .ToList();

        var pricing = new List<PricingOptionSnapshot>();
        foreach (var p in o.PricingOptions)
        {
            if (!page.Amounts.Contains(p.Amount))
            {
                warnings.Add(page.CrossedOutAmounts.Contains(p.Amount)
                    ? $"{scope}: price {p.Amount} for '{p.Name}' is crossed out on the page (old price) and was dropped."
                    : $"{scope}: price {p.Amount} for '{p.Name}' is not on the page and was dropped.");
                continue;
            }

            if (p.CompareAtAmount is { } compareAt && compareAt > p.Amount &&
                (page.CrossedOutAmounts.Contains(compareAt) || page.Amounts.Contains(compareAt)))
                attributes.Add(new OfferingAttribute
                {
                    Key = "regular_price", Label = "Regular price",
                    Value = compareAt.ToString(CultureInfo.InvariantCulture), Unit = p.Currency
                });

            pricing.Add(new PricingOptionSnapshot
            {
                // Payment links are stable ids of a price in the payment provider
                ExternalId = p.CheckoutUrl is not null && IsPageCheckout(page, p.CheckoutUrl) ? p.CheckoutUrl : Slug.From(p.Name),
                Name = p.Name,
                Amount = p.Amount,
                Currency = ValidCurrency(p.Currency) ?? defaultCurrency,
                BillingType = p.BillingType,
                // Interval and installments only make sense for repeated charges
                BillingInterval = p.BillingType == BillingType.OneTime ? null : p.BillingInterval,
                InstallmentCount = p.BillingType == BillingType.Installments ? p.InstallmentCount : null,
                CheckoutUrl = ResolveCheckout(page, p.CheckoutUrl, scope, warnings)
            });
        }

        pricing = pricing.DistinctBy(p => p.ExternalId).ToList();

        // A payment link charges a single price, so several prices on one link means a misread
        foreach (var shared in pricing.Where(p => p.CheckoutUrl is not null).GroupBy(p => p.CheckoutUrl).Where(g => g.Count() > 1))
            warnings.Add($"{scope}: prices [{string.Join(", ", shared.Select(p => p.Amount))}] share one checkout link; needs review.");
        if (pricing.Count > 0)
            pricing[0] = pricing[0] with { IsDefault = true };
        else
            warnings.Add($"{scope}: no verified price.");

        var unknownStaff = o.StaffNames.Select(Slug.From).Where(id => !staffIds.Contains(id)).ToList();
        if (unknownStaff.Count > 0)
            warnings.Add($"{scope}: unknown staff references [{string.Join(", ", unknownStaff)}] were dropped.");

        return new OfferingSnapshot
        {
            ExternalId = externalId,
            Slug = slug,
            Name = o.Name,
            Aliases = CleanList(o.Aliases, exclude: o.Name),
            Kind = o.Kind,
            ShortDescription = o.ShortDescription,
            Description = o.Description,
            Category = o.Category,
            Tags = CleanList(o.Tags.Select(t => t.ToLowerInvariant())),
            DeliveryMode = o.DeliveryMode,
            Duration = ValidDuration(o.Duration, scope, warnings),
            SessionCount = o.SessionCount is > 0 ? o.SessionCount : null,
            Audience = new Audience
            {
                LevelText = o.LevelText,
                Prerequisites = o.Prerequisites,
                AgeMin = o.AgeMin,
                AgeMax = o.AgeMax
            },
            Inclusions = CleanList(o.Inclusions),
            Attributes = attributes,
            Faq = o.Faq.Select(f => new FaqItem { Question = f.Question, Answer = f.Answer }).ToList(),
            Url = page.CanonicalUrl ?? page.Url.ToString(),
            ImageUrl = page.ImageUrl,
            StaffExternalIds = o.StaffNames.Select(Slug.From).Where(staffIds.Contains).Distinct().ToList(),
            PricingOptions = pricing,
            Instances = o.Instances.Select(i => MapInstance(i, scope, warnings)).DistinctBy(i => i.ExternalId).ToList()
        };
    }

    private static OfferingInstanceSnapshot MapInstance(ExtractedInstance i, string scope, List<string> warnings)
    {
        var start = ValidDate(i.StartDate, $"{scope} start date", warnings);
        var schedule = new List<ScheduleRule>();

        foreach (var rule in i.Schedule)
        {
            var tz = ValidTimeZone(rule.TimeZone, warnings, $"{scope} schedule");
            if (tz is null || rule.DaysOfWeek.Count == 0 || rule.DurationMinutes <= 0 ||
                !TimeOnly.TryParseExact(rule.StartTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                warnings.Add($"{scope}: invalid schedule rule '{rule.StartTime} {rule.TimeZone}' was dropped.");
                continue;
            }

            schedule.Add(new ScheduleRule
            {
                DaysOfWeek = rule.DaysOfWeek.Distinct().Order().ToList(),
                StartTime = time,
                DurationMinutes = rule.DurationMinutes,
                TimeZone = tz
            });
        }

        return new OfferingInstanceSnapshot
        {
            ExternalId = start?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? Slug.From(i.Name),
            Name = i.Name,
            StartDate = start,
            EndDate = ValidDate(i.EndDate, $"{scope} end date", warnings),
            Schedule = schedule,
            EnrollmentStatus = i.EnrollmentStatus
        };
    }

    /// <summary>Checkout is owned by the deterministic layer: the LLM may only pick a link that exists on the page.</summary>
    private static string? ResolveCheckout(WebPage page, string? llmUrl, string scope, List<string> warnings)
    {
        if (llmUrl is not null && IsPageCheckout(page, llmUrl))
            return llmUrl;

        if (llmUrl is not null)
            warnings.Add($"{scope}: checkout URL '{llmUrl}' is not a checkout link on the page and was replaced.");

        var distinct = page.CheckoutLinks.Select(l => l.Url).Distinct().ToList();
        if (distinct.Count == 1)
            return distinct[0];

        if (distinct.Count > 1)
            warnings.Add($"{scope}: several checkout links on the page; checkout left empty for review.");

        return null;
    }

    private static bool IsPageCheckout(WebPage page, string url) =>
        page.CheckoutLinks.Any(l => string.Equals(l.Url, url, StringComparison.OrdinalIgnoreCase));

    private static bool Mentioned(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static List<string> CleanList(IEnumerable<string> values, string? exclude = null) =>
        values.Select(v => v.Trim())
            .Where(v => v.Length > 0 && !string.Equals(v, exclude, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string? ValidCurrency(string? value) =>
        value is { Length: 3 } && value.All(char.IsAsciiLetter) ? value.ToUpperInvariant() : null;

    private static string? ValidTimeZone(string? value, List<string> warnings, string scope)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (TimeZoneInfo.TryFindSystemTimeZoneById(value, out _) && value.Contains('/'))
            return value;

        warnings.Add($"{scope}: '{value}' is not a valid IANA time zone.");
        return null;
    }

    private static DateOnly? ValidDate(string? value, string scope, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        warnings.Add($"{scope}: '{value}' is not a valid date.");
        return null;
    }

    private static string? ValidDuration(string? value, string scope, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (IsoDurationRegex().IsMatch(value))
            return value;

        warnings.Add($"{scope}: '{value}' is not an ISO 8601 duration.");
        return null;
    }

    [GeneratedRegex(@"^P(?!$)(\d+Y)?(\d+M)?(\d+W)?(\d+D)?(T(?=\d)(\d+H)?(\d+M)?(\d+S)?)?$")]
    private static partial Regex IsoDurationRegex();
}

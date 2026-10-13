using Microsoft.EntityFrameworkCore;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Ingestion;

public sealed record CatalogImportResult(int Created, int Updated, int Deactivated);

/// <summary>
/// Upserts a <see cref="CatalogSnapshot"/> into the database. Shared by all adapters.
/// Rows are matched by (SourcePlatform, ExternalId); rows from the same platform that are
/// missing in the snapshot are deactivated, never deleted. Rows from other platforms are untouched.
/// </summary>
public class CatalogImporter(CatalogDbContext db)
{
    private int _created, _updated, _deactivated;

    public async Task<CatalogImportResult> ImportAsync(CatalogSource source, CatalogSnapshot snapshot, CancellationToken ct = default)
    {
        Validate(snapshot);
        _created = _updated = _deactivated = 0;

        var platform = source.Platform;
        var now = DateTimeOffset.UtcNow;

        var business = await db.Businesses
            .Include(b => b.Staff)
            .Include(b => b.Offerings).ThenInclude(o => o.PricingOptions)
            .Include(b => b.Offerings).ThenInclude(o => o.Instances)
            .Include(b => b.Offerings).ThenInclude(o => o.Staff)
            .AsSplitQuery()
            .FirstOrDefaultAsync(b => b.Slug == source.BusinessSlug, ct);

        if (business is null)
        {
            business = new Business
            {
                Slug = source.BusinessSlug,
                Name = snapshot.Business.Name,
                SourcePlatform = platform,
                ExternalId = snapshot.Business.ExternalId
            };
            db.Businesses.Add(business);
            _created++;
        }

        // A business can aggregate several sources; only the source that created it owns its profile
        if (business.SourcePlatform == platform)
        {
            ApplyBusiness(business, snapshot.Business);
            business.VerifiedAt = now;
        }

        var staffByExternalId = new Dictionary<string, Staff>();
        foreach (var s in snapshot.Staff)
        {
            var staff = FindOrCreate(business.Staff, platform, s.ExternalId,
                () => new Staff { Name = s.Name, Business = business });
            ApplyStaff(staff, s);
            staff.VerifiedAt = now;
            staffByExternalId[s.ExternalId] = staff;
        }

        foreach (var o in snapshot.Offerings)
        {
            var offering = FindOrCreate(business.Offerings, platform, o.ExternalId,
                () => new Offering { Slug = o.Slug, Name = o.Name, Business = business, Status = o.Status ?? OfferingStatus.Draft });
            ApplyOffering(offering, o, staffByExternalId);
            offering.VerifiedAt = now;

            foreach (var p in o.PricingOptions)
            {
                var pricing = FindOrCreate(offering.PricingOptions, platform, p.ExternalId,
                    () => new PricingOption { Name = p.Name, Currency = p.Currency, Offering = offering });
                ApplyPricing(pricing, p);
                pricing.VerifiedAt = now;
            }

            foreach (var i in o.Instances)
            {
                var instance = FindOrCreate(offering.Instances, platform, i.ExternalId,
                    () => new OfferingInstance { Name = i.Name, Offering = offering });
                ApplyInstance(instance, i);
                instance.VerifiedAt = now;
            }

            DeactivateMissing(offering.PricingOptions, platform, o.PricingOptions.Select(p => p.ExternalId),
                p => p.IsActive, p => p.IsActive = false);
            DeactivateMissing(offering.Instances, platform, o.Instances.Select(i => i.ExternalId),
                i => i.EnrollmentStatus != EnrollmentStatus.Closed, i => i.EnrollmentStatus = EnrollmentStatus.Closed);
        }

        DeactivateMissing(business.Offerings, platform, snapshot.Offerings.Select(o => o.ExternalId),
            o => o.Status != OfferingStatus.Archived, o => o.Status = OfferingStatus.Archived);

        await db.SaveChangesAsync(ct);

        return new CatalogImportResult(_created, _updated, _deactivated);
    }

    private T FindOrCreate<T>(List<T> items, SourcePlatform platform, string externalId, Func<T> create)
        where T : CatalogEntity
    {
        var existing = items.FirstOrDefault(x => x.SourcePlatform == platform && x.ExternalId == externalId);
        if (existing is not null)
        {
            _updated++;
            return existing;
        }

        var created = create();
        created.SourcePlatform = platform;
        created.ExternalId = externalId;
        items.Add(created);
        _created++;
        return created;
    }

    private void DeactivateMissing<T>(List<T> items, SourcePlatform platform, IEnumerable<string> presentExternalIds,
        Func<T, bool> isActive, Action<T> deactivate)
        where T : CatalogEntity
    {
        var present = presentExternalIds.ToHashSet();

        foreach (var item in items.Where(x => x.SourcePlatform == platform && !present.Contains(x.ExternalId!) && isActive(x)))
        {
            deactivate(item);
            _deactivated++;
        }
    }

    private static void ApplyBusiness(Business target, BusinessSnapshot s)
    {
        target.Name = s.Name;
        target.Aliases = [.. s.Aliases];
        target.Description = s.Description;
        target.WebsiteUrl = s.WebsiteUrl;
        target.DefaultCurrency = s.DefaultCurrency;
        target.TimeZone = s.TimeZone;
        target.Language = s.Language;
    }

    private static void ApplyStaff(Staff target, StaffSnapshot s)
    {
        target.Name = s.Name;
        target.Role = s.Role;
        target.Bio = s.Bio;
        target.Credentials = s.Credentials;
        target.ImageUrl = s.ImageUrl;
    }

    private static void ApplyOffering(Offering target, OfferingSnapshot s, Dictionary<string, Staff> staffByExternalId)
    {
        // Status is intentionally not overwritten: it is changed by review, not by the source
        if (target.Status == OfferingStatus.Archived)
            target.Status = OfferingStatus.Draft; // reappeared in the source, needs review again

        target.Slug = s.Slug;
        target.Name = s.Name;
        target.Aliases = [.. s.Aliases];
        target.Kind = s.Kind;
        target.ShortDescription = s.ShortDescription;
        target.Description = s.Description;
        target.Category = s.Category;
        target.Tags = [.. s.Tags];
        target.DeliveryMode = s.DeliveryMode;
        target.Duration = s.Duration;
        target.SessionCount = s.SessionCount;
        target.Audience = s.Audience;
        target.Inclusions = [.. s.Inclusions];
        target.Attributes = [.. s.Attributes];
        target.Faq = [.. s.Faq];
        target.Url = s.Url;
        target.ImageUrl = s.ImageUrl;
        target.Staff = [.. s.StaffExternalIds.Select(id => staffByExternalId[id])];
    }

    private static void ApplyPricing(PricingOption target, PricingOptionSnapshot s)
    {
        target.Name = s.Name;
        target.Amount = s.Amount;
        target.Currency = s.Currency;
        target.BillingType = s.BillingType;
        target.BillingInterval = s.BillingInterval;
        target.InstallmentCount = s.InstallmentCount;
        target.IsDefault = s.IsDefault;
        target.IsActive = true;
        target.CheckoutUrl = s.CheckoutUrl;
    }

    private static void ApplyInstance(OfferingInstance target, OfferingInstanceSnapshot s)
    {
        target.Name = s.Name;
        target.StartDate = s.StartDate;
        target.EndDate = s.EndDate;
        target.Schedule = [.. s.Schedule];
        target.EnrollmentStatus = s.EnrollmentStatus;
        target.EnrollmentDeadline = s.EnrollmentDeadline;
        target.Capacity = s.Capacity;
        target.SeatsLeft = s.SeatsLeft;
    }

    /// <summary>Rejects snapshots that would corrupt matching or violate DB constraints.</summary>
    private static void Validate(CatalogSnapshot snapshot)
    {
        var errors = new List<string>();

        void RequireUnique(IEnumerable<string> ids, string scope)
        {
            foreach (var dup in ids.GroupBy(id => id).Where(g => g.Key.Length == 0 || g.Count() > 1))
                errors.Add(dup.Key.Length == 0 ? $"{scope}: empty ExternalId" : $"{scope}: duplicate ExternalId '{dup.Key}'");
        }

        RequireUnique(snapshot.Staff.Select(s => s.ExternalId), "Staff");
        RequireUnique(snapshot.Offerings.Select(o => o.ExternalId), "Offerings");

        var staffIds = snapshot.Staff.Select(s => s.ExternalId).ToHashSet();

        foreach (var o in snapshot.Offerings)
        {
            RequireUnique(o.PricingOptions.Select(p => p.ExternalId), $"Offering '{o.ExternalId}' pricing");
            RequireUnique(o.Instances.Select(i => i.ExternalId), $"Offering '{o.ExternalId}' instances");

            errors.AddRange(o.StaffExternalIds.Where(id => !staffIds.Contains(id))
                .Select(id => $"Offering '{o.ExternalId}': unknown staff '{id}'"));

            errors.AddRange(o.PricingOptions.Where(p => p.Currency.Length != 3 || p.Amount < 0)
                .Select(p => $"Offering '{o.ExternalId}': invalid price '{p.ExternalId}'"));
        }

        if (errors.Count > 0)
            throw new InvalidOperationException("Invalid catalog snapshot: " + string.Join("; ", errors));
    }
}

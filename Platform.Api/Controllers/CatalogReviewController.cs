using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Entities;

namespace Platform.Api.Controllers;

/// <summary>
/// Review step after import: imported offerings start as Draft and a person can check them and set Active
/// (or Archived to hide them). Also allows switching off pricing options misread by the importer.
/// </summary>
[ApiController]
[Route("api/catalog")]
public class CatalogReviewController(CatalogDbContext db) : ControllerBase
{
    public record PricingReview(Guid Id, string Name, decimal Amount, string Currency, BillingType BillingType, bool IsActive);

    public record OfferingReview(
        string BusinessSlug,
        string Slug,
        string Name,
        OfferingStatus Status,
        SourcePlatform SourcePlatform,
        DateTimeOffset? VerifiedAt,
        IReadOnlyList<PricingReview> PricingOptions);

    public record StatusRequest(OfferingStatus Status);

    public record ActiveRequest(bool IsActive);

    [HttpGet("offerings")]
    public async Task<IReadOnlyList<OfferingReview>> GetOfferings(CancellationToken ct) =>
        await db.Offerings.AsNoTracking()
            .OrderBy(o => o.Business.Slug).ThenBy(o => o.Slug)
            .Select(o => new OfferingReview(
                o.Business.Slug, o.Slug, o.Name, o.Status, o.SourcePlatform, o.VerifiedAt,
                o.PricingOptions
                    .OrderBy(p => p.Amount)
                    .Select(p => new PricingReview(p.Id, p.Name, p.Amount, p.Currency, p.BillingType, p.IsActive))
                    .ToList()))
            .ToListAsync(ct);

    [HttpPut("businesses/{businessSlug}/offerings/{offeringSlug}/status")]
    public async Task<IActionResult> SetStatus(string businessSlug, string offeringSlug, [FromBody] StatusRequest request, CancellationToken ct)
    {
        var offering = await db.Offerings
            .FirstOrDefaultAsync(o => o.Business.Slug == businessSlug && o.Slug == offeringSlug, ct);
        if (offering is null)
            return NotFound();

        offering.Status = request.Status;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPut("pricing-options/{id:guid}/active")]
    public async Task<IActionResult> SetPricingActive(Guid id, [FromBody] ActiveRequest request, CancellationToken ct)
    {
        var pricing = await db.PricingOptions.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pricing is null)
            return NotFound();

        pricing.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

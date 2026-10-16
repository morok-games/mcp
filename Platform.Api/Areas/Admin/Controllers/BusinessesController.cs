using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Areas.Admin.Models;
using Platform.Api.Catalog;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Entities;
using Platform.Api.Catalog.Ingestion;
using Platform.Api.Mcp;

namespace Platform.Api.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize]
[AutoValidateAntiforgeryToken]
public class BusinessesController(
    CatalogDbContext db,
    CatalogIngestionService ingestion,
    CatalogAdapterResolver adapters,
    McpToolDescriptions mcpToolDescriptions,
    ILogger<BusinessesController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await db.Businesses.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new BusinessRow(
                b.Id, b.Name,
                b.Sources
                    .OrderBy(s => s.CreatedAt)
                    .Select(s => new BusinessSourceRow(s.Platform, s.Url, s.LastImportedAt, s.LastImportSucceeded, s.LastImportSummary))
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return View(rows);
    }

    /// <summary>What the import produced for a business: its offerings and staff.</summary>
    public async Task<IActionResult> Details(Guid id, CancellationToken ct)
    {
        var business = await db.Businesses.AsNoTracking()
            .Where(b => b.Id == id)
            .Include(b => b.Offerings).ThenInclude(o => o.PricingOptions)
            .Include(b => b.Offerings).ThenInclude(o => o.Staff)
            .Include(b => b.Staff)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);

        if (business is null)
            return NotFound();

        var offerings = business.Offerings
            .OrderBy(o => o.Status == OfferingStatus.Archived).ThenBy(o => o.Name)
            .Select(o => new OfferingRow(
                o.Name, o.Kind, o.Status, o.DeliveryMode, o.Duration, o.SessionCount, o.Audience.LevelText,
                o.PricingOptions
                    .Where(p => p.IsActive)
                    .OrderByDescending(p => p.IsDefault).ThenBy(p => p.Amount)
                    .Select(p => $"{p.Name}: {p.Amount:0.##} {p.Currency} ({p.BillingType})")
                    .ToList(),
                o.Staff.Select(s => s.Name).OrderBy(n => n).ToList()))
            .ToList();

        var staff = business.Staff
            .OrderBy(s => s.Name)
            .Select(s => new StaffRow(s.Name, s.Role, s.Credentials))
            .ToList();

        // TODO: remove with multi-page sources; until then two pages of one site are separate businesses
        var duplicates = business.ExternalId is null
            ? []
            : await db.Businesses.AsNoTracking()
                .Where(b => b.Id != business.Id && b.SourcePlatform == business.SourcePlatform && b.ExternalId == business.ExternalId)
                .OrderBy(b => b.Name)
                .Select(b => new DuplicateRow(b.Id, b.Name))
                .ToListAsync(ct);

        return View(new BusinessDetails(business.Name, duplicates, offerings, staff));
    }

    [HttpGet]
    public IActionResult Create()
    {
        SetPlatforms();
        return View(new BusinessCreateModel());
    }

    [HttpPost]
    public async Task<IActionResult> Create(BusinessCreateModel model, CancellationToken ct)
    {
        var baseSlug = Slug.From(model.Name);
        if (baseSlug.Length == 0)
            ModelState.AddModelError(nameof(model.Name), "Name must contain letters or digits.");

        if (!adapters.SupportedPlatforms.Contains(model.SourcePlatform))
            ModelState.AddModelError(nameof(model.SourcePlatform), "No import adapter for this platform.");

        if (!Uri.TryCreate(model.SourceUrl, UriKind.Absolute, out var sourceUri) ||
            sourceUri.Scheme is not ("http" or "https"))
            ModelState.AddModelError(nameof(model.SourceUrl), "Enter an http(s) link.");

        if (!ModelState.IsValid)
        {
            SetPlatforms();
            return View(model);
        }

        var now = DateTimeOffset.UtcNow;
        var business = new Business
        {
            Slug = await UniqueSlugAsync(baseSlug, ct),
            Name = model.Name.Trim(),
            // Owned by the source platform, so the import fills the profile: aliases, time zone, currency,
            // description (and refines the name as the site presents it)
            SourcePlatform = model.SourcePlatform,
            WebsiteUrl = sourceUri!.GetLeftPart(UriPartial.Authority),
            Sources =
            [
                new BusinessSource
                {
                    Platform = model.SourcePlatform,
                    Url = sourceUri.ToString(),
                    ExtractionInstructions = NormalizeInstructions(model.ExtractionInstructions),
                    CreatedAt = now
                }
            ]
        };

        db.Businesses.Add(business);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = $"Business '{business.Name}' created. Run the import to load its offerings.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(Guid id, CancellationToken ct)
    {
        var model = await db.Businesses.AsNoTracking()
            .Where(b => b.Id == id)
            .Select(b => new BusinessEditModel
            {
                Id = b.Id,
                Name = b.Name,
                SourcePlatform = b.Sources.OrderBy(s => s.CreatedAt).Select(s => s.Platform).FirstOrDefault(),
                SourceUrl = b.Sources.OrderBy(s => s.CreatedAt).Select(s => s.Url).FirstOrDefault(),
                ExtractionInstructions = b.Sources.OrderBy(s => s.CreatedAt).Select(s => s.ExtractionInstructions).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return model is null ? NotFound() : View(model);
    }

    /// <summary>Saves only the extraction instructions; name, link and adapter are not editable.</summary>
    [HttpPost]
    public async Task<IActionResult> Edit(Guid id, BusinessEditModel model, CancellationToken ct)
    {
        var business = await db.Businesses.Include(b => b.Sources).FirstOrDefaultAsync(b => b.Id == id, ct);
        var source = business?.Sources.OrderBy(s => s.CreatedAt).FirstOrDefault();
        if (business is null || source is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            // Read-only fields come from the database, not from the posted form
            model.Name = business.Name;
            model.SourcePlatform = source.Platform;
            model.SourceUrl = source.Url;
            return View(model);
        }

        source.ExtractionInstructions = NormalizeInstructions(model.ExtractionInstructions);
        await db.SaveChangesAsync(ct);

        TempData["Success"] = $"Extraction instructions for '{business.Name}' saved. Run the import to apply them.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var name = await db.Businesses.Where(b => b.Id == id).Select(b => b.Name).FirstOrDefaultAsync(ct);
        if (name is null)
            return NotFound();

        // Offerings, staff, prices, runs and sources are removed by ON DELETE CASCADE in the database
        await db.Businesses.Where(b => b.Id == id).ExecuteDeleteAsync(ct);
        mcpToolDescriptions.Invalidate(); // the business must disappear from the MCP tool description
        logger.LogInformation("Business {Name} ({Id}) deleted by {Admin}", name, id, User.Identity?.Name);

        TempData["Success"] = $"Business '{name}' deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Import(Guid id, CancellationToken ct)
    {
        var business = await db.Businesses.Include(b => b.Sources).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (business is null)
            return NotFound();

        var source = business.Sources.OrderBy(s => s.CreatedAt).FirstOrDefault();
        if (source?.Url is null)
        {
            TempData["Error"] = $"'{business.Name}' has no import source.";
            return RedirectToAction(nameof(Index));
        }

        var sourceId = source.Id;
        string? summary;
        bool succeeded;

        try
        {
            var (snapshot, result) = await ingestion.IngestAsync(
                new CatalogSource(source.Platform, business.Slug, new Uri(source.Url),
                    ExtractionInstructions: source.ExtractionInstructions), ct);

            succeeded = true;
            summary = snapshot.Warnings.Count > 0 ? string.Join("\n", snapshot.Warnings) : null;
            mcpToolDescriptions.Invalidate(); // offerings changed: refresh the MCP tool description

            TempData["Success"] = $"Import for '{business.Name}' finished: {result.Created} created, {result.Updated} updated, " +
                                  $"{result.Deactivated} deactivated.";
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or NotSupportedException or DbUpdateException)
        {
            logger.LogError(ex, "Import failed for {Business}", business.Slug);
            succeeded = false;
            // For database errors the useful text (e.g. a violated constraint) is in the inner exception
            summary = ex is DbUpdateException { InnerException: { } inner } ? $"Database error: {inner.Message}" : ex.Message;
            TempData["Error"] = $"Import for '{business.Name}' failed: {summary}";

            // Do not persist half-applied import changes together with the status below
            db.ChangeTracker.Clear();
        }

        var tracked = await db.BusinessSources.FirstAsync(s => s.Id == sourceId, ct);
        tracked.LastImportedAt = DateTimeOffset.UtcNow;
        tracked.LastImportSucceeded = succeeded;
        tracked.LastImportSummary = summary;
        await db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    private static string? NormalizeInstructions(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>"victory-skating", then "victory-skating-2", "victory-skating-3", ... if taken.</summary>
    private async Task<string> UniqueSlugAsync(string baseSlug, CancellationToken ct)
    {
        var taken = await db.Businesses
            .Where(b => b.Slug == baseSlug || b.Slug.StartsWith(baseSlug + "-"))
            .Select(b => b.Slug)
            .ToListAsync(ct);

        if (!taken.Contains(baseSlug))
            return baseSlug;

        var suffix = 2;
        while (taken.Contains($"{baseSlug}-{suffix}"))
            suffix++;

        return $"{baseSlug}-{suffix}";
    }

    private void SetPlatforms() =>
        ViewBag.Platforms = adapters.SupportedPlatforms
            .OrderBy(p => p.ToString())
            .Select(p => new SelectListItem(p.ToString(), p.ToString()))
            .ToList();
}

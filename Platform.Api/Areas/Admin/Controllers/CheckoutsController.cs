using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Areas.Admin.Models;
using Platform.Api.Catalog.Data;

namespace Platform.Api.Areas.Admin.Controllers;

/// <summary>Checkout links issued by the chat and MCP, and whether users followed them to the payment page.</summary>
[Area("Admin")]
[Authorize]
public class CheckoutsController(CatalogDbContext db) : Controller
{
    private const int Limit = 100;

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await db.CheckoutSessions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAt)
            .Take(Limit)
            .Select(s => new CheckoutRow(
                s.CreatedAt, s.Business.Name, s.Offering.Name, s.Amount, s.Currency,
                s.Channel, s.ClientName, s.Status, s.OpenCount))
            .ToListAsync(ct);

        return View(rows);
    }
}

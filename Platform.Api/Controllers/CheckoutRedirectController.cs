using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platform.Api.Catalog.Checkout;
using Platform.Api.Catalog.Data;

namespace Platform.Api.Controllers;

/// <summary>Tracked checkout links: records the transition and forwards the user to the business's payment page.</summary>
[ApiController]
[Route("go")]
public class CheckoutRedirectController(CatalogDbContext db, TimeProvider time, ILogger<CheckoutRedirectController> logger)
    : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Go(Guid id, CancellationToken ct)
    {
        var session = await db.CheckoutSessions.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session is null)
            return NotFound("This checkout link is not valid.");

        session.OpenCount++;
        if (session.OpenedAt is null)
        {
            session.OpenedAt = time.GetUtcNow();
            session.Status = CheckoutSessionStatus.Opened;
        }
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Checkout {Session} opened ({Channel}, {Client}), visit #{Count}",
            session.Id, session.Channel, session.ClientName, session.OpenCount);

        // The destination comes from our database, never from the request: no open redirect
        return Redirect(session.DestinationUrl);
    }
}

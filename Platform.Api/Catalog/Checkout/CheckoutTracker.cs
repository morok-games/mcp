using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Platform.Api.Catalog.Data;
using Platform.Api.Catalog.Search;

namespace Platform.Api.Catalog.Checkout;

public class CheckoutOptions
{
    public const string SectionName = "Checkout";

    /// <summary>
    /// Public address of this app for tracked links, e.g. the tunnel or production URL.
    /// Empty: taken from the current request.
    /// </summary>
    public string PublicBaseUrl { get; set; } = "";
}

/// <summary>Issues checkout links that go through our redirect, so every transition to payment is recorded.</summary>
public class CheckoutTracker(
    CatalogDbContext db,
    IHttpContextAccessor httpContextAccessor,
    IOptions<CheckoutOptions> options,
    TimeProvider time)
{
    /// <returns>Tracked link (https://our-host/go/{id}) to give to the user instead of the payment page URL.</returns>
    public async Task<string> CreateLinkAsync(PriceCard price, CheckoutChannel channel, string? clientName, CancellationToken ct = default)
    {
        var target = await db.PricingOptions.AsNoTracking()
            .Where(p => p.Id == price.Id)
            .Select(p => new { p.OfferingId, p.Offering.BusinessId })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException($"Pricing option {price.Id} does not exist.");

        var session = new CheckoutSession
        {
            BusinessId = target.BusinessId,
            OfferingId = target.OfferingId,
            PricingOptionId = price.Id,
            Amount = price.Amount,
            Currency = price.Currency,
            Channel = channel,
            ClientName = Truncate(clientName, 256),
            DestinationUrl = "",
            CreatedAt = time.GetUtcNow()
        };

        // Stripe Payment Links pass client_reference_id to their payment webhooks: ready for matching payments later
        session.DestinationUrl = QueryHelpers.AddQueryString(price.CheckoutUrl!, "client_reference_id", session.Id.ToString("N"));

        db.CheckoutSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return $"{BaseUrl()}/go/{session.Id:N}";
    }

    private string BaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.PublicBaseUrl))
            return options.Value.PublicBaseUrl.TrimEnd('/');

        var request = httpContextAccessor.HttpContext?.Request
                      ?? throw new InvalidOperationException("Checkout:PublicBaseUrl is required outside of an HTTP request.");
        return $"{request.Scheme}://{request.Host}";
    }

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}

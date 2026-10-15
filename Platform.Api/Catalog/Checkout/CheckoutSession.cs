using Platform.Api.Catalog.Entities;

namespace Platform.Api.Catalog.Checkout;

public enum CheckoutChannel
{
    /// <summary>Checkout button in our own chat.</summary>
    WebChat,
    /// <summary>Link returned by the create_checkout MCP tool to an external AI client.</summary>
    Mcp
}

public enum CheckoutSessionStatus
{
    /// <summary>The checkout link was issued to the user.</summary>
    Created,
    /// <summary>The user followed the link to the payment page.</summary>
    Opened
    // Paid will come with payment provider webhooks (needs access to the business's Stripe account)
}

/// <summary>
/// One attempt to enroll: a checkout link issued to a user and whether they followed it.
/// The link points to our redirect (/go/{Id}) so the transition to the payment page can be recorded.
/// </summary>
public class CheckoutSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    public Guid OfferingId { get; set; }
    public Offering Offering { get; set; } = null!;

    public Guid PricingOptionId { get; set; }
    public PricingOption PricingOption { get; set; } = null!;

    /// <summary>Price at the moment the link was issued; the catalog price may change later.</summary>
    public decimal Amount { get; set; }

    public required string Currency { get; set; }

    public CheckoutChannel Channel { get; set; }

    /// <summary>Which client issued the link, e.g. MCP client name ("ChatGPT") or its User-Agent.</summary>
    public string? ClientName { get; set; }

    /// <summary>Payment page the redirect leads to, including our client_reference_id.</summary>
    public required string DestinationUrl { get; set; }

    public CheckoutSessionStatus Status { get; set; } = CheckoutSessionStatus.Created;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>First time the user followed the link.</summary>
    public DateTimeOffset? OpenedAt { get; set; }

    public int OpenCount { get; set; }
}

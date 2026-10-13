namespace Platform.Api.Catalog.Entities;

/// <summary>One way to pay for an offering, e.g. "Pay in full" or "6 monthly payments".</summary>
public class PricingOption : CatalogEntity
{
    public Guid OfferingId { get; set; }
    public Offering Offering { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Amount per charge (per installment for Recurring/Installments).</summary>
    public decimal Amount { get; set; }

    /// <summary>ISO 4217 code, e.g. "USD".</summary>
    public required string Currency { get; set; }

    public BillingType BillingType { get; set; }

    public BillingInterval? BillingInterval { get; set; }

    /// <summary>Number of charges for Installments; null for open-ended subscriptions.</summary>
    public int? InstallmentCount { get; set; }

    public bool IsDefault { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>External checkout page for this option (used until native checkout integrations exist).</summary>
    public string? CheckoutUrl { get; set; }
}

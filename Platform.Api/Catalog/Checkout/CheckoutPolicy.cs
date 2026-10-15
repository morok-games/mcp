using System.Globalization;
using Platform.Api.Catalog.Entities;
using Platform.Api.Catalog.Search;

namespace Platform.Api.Catalog.Checkout;

/// <param name="Price">Pricing option to pay for; null when checkout is not possible.</param>
/// <param name="Reason">Why checkout is not possible, in words the assistant can pass to the user.</param>
public sealed record CheckoutDecision(PriceCard? Price, string? Reason)
{
    public bool CanCheckout => Price is not null;
}

/// <summary>
/// Shared enrollment rules for every channel (our chat, MCP clients like ChatGPT): whether an offering
/// can be bought now and which price and checkout link to use.
/// </summary>
public static class CheckoutPolicy
{
    /// <param name="pricingOptionId">Specific option chosen by the user; null picks the default one.</param>
    public static CheckoutDecision Decide(OfferingCard offering, Guid? pricingOptionId = null)
    {
        if (!CanEnroll(offering))
            return new CheckoutDecision(null,
                $"Enrollment for {offering.Name} is {offering.CurrentInstance!.EnrollmentStatus.ToString().ToLowerInvariant()} right now.");

        var withCheckout = offering.PricingOptions.Where(p => p.CheckoutUrl is not null).ToList();

        if (pricingOptionId is { } id)
        {
            var chosen = withCheckout.FirstOrDefault(p => p.Id == id);
            return chosen is not null
                ? new CheckoutDecision(chosen, null)
                : new CheckoutDecision(null, $"Pricing option {id} is not available for online checkout.");
        }

        var price = withCheckout.FirstOrDefault(p => p.IsDefault) ?? withCheckout.FirstOrDefault();
        return price is not null
            ? new CheckoutDecision(price, null)
            : new CheckoutDecision(null, $"{offering.Name} has no online checkout; contact the business directly.");
    }

    /// <summary>Enrollment is possible when the current run is open, or when no runs are published (rolling enrollment).</summary>
    public static bool CanEnroll(OfferingCard offering) =>
        offering.CurrentInstance is null || offering.CurrentInstance.EnrollmentStatus == EnrollmentStatus.Open;

    /// <summary>"$299", "$49/month", "6 × $50".</summary>
    public static string FormatPrice(PriceCard price)
    {
        var amount = price.Amount.ToString("0.##", CultureInfo.InvariantCulture);
        var symbol = price.Currency == "USD" ? "$" : price.Currency + " ";

        return price.BillingType switch
        {
            BillingType.Recurring when price.BillingInterval is { } interval => $"{symbol}{amount}/{interval.ToString().ToLowerInvariant()}",
            BillingType.Installments when price.InstallmentCount is { } count => $"{count} × {symbol}{amount}",
            _ => $"{symbol}{amount}"
        };
    }
}

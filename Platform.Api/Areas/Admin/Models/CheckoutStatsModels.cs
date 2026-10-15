using Platform.Api.Catalog.Checkout;

namespace Platform.Api.Areas.Admin.Models;

public record CheckoutRow(
    DateTimeOffset CreatedAt,
    string Brand,
    string Offering,
    decimal Amount,
    string Currency,
    CheckoutChannel Channel,
    string? ClientName,
    CheckoutSessionStatus Status,
    int OpenCount);

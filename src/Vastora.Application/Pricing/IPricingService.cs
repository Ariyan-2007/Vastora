using Vastora.Domain.Entities;

namespace Vastora.Application.Pricing;

/// <summary>
/// The single place a basket turns into money. Used by both the cart preview and checkout, which
/// closes the gap the Shop blueprint documented — the cart endpoint used to return a raw subtotal
/// with no way to show the customer their discount before committing to a purchase.
///
/// Order of operations, and it matters: line prices (with variant overrides and customer-group
/// pricing) → discounts (promotions, then the legacy coupon) → shipping → tax on the discounted
/// base → gift cards and store credit against the final total. Taxing before discounting would
/// overcharge; settling gift cards before tax would under-collect.
/// </summary>
public interface IPricingService
{
    /// <summary>Resolves cart items to live products, applying variant price overrides and group pricing.</summary>
    Task<IReadOnlyList<ResolvedLine>> ResolveLinesAsync(
        string businessId,
        IReadOnlyList<CartItem> items,
        string? customerUserId,
        CancellationToken ct = default);

    Task<PriceBreakdown> PriceAsync(
        PricingContext context,
        IReadOnlyList<ResolvedLine> lines,
        CancellationToken ct = default);

    /// <summary>
    /// §9.43. Public, currently-live coupon and promotion codes worth showing a shopper before
    /// they've typed anything — "shown where applicable" rather than requiring them to already
    /// know a code exists. Filtered down to what <paramref name="subtotal"/> can actually
    /// qualify for on <c>MinOrderAmount</c> alone; applying still runs the full check (customer
    /// group, first-order-only, scope) the same way it always did, so this is a helpful shortlist,
    /// not a second source of truth for eligibility.
    /// </summary>
    Task<List<AvailableOfferResponse>> GetAvailableOffersAsync(
        string businessId, decimal subtotal, CancellationToken ct = default);
}

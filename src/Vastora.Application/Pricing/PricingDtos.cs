using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Pricing;

/// <summary>One resolved cart line: the product/variant, its price, and what it costs the seller.</summary>
public record ResolvedLine(
    Product Product,
    ProductVariant? Variant,
    string ProductId,
    string? VariantId,
    string ProductName,
    string? VariantSummary,
    decimal UnitPrice,
    decimal? UnitCost,
    int Quantity,
    decimal WeightKg)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// The complete money breakdown for a basket, computed the same way for a cart preview and for a
/// real checkout — one implementation, so what the customer is quoted and what they are charged
/// cannot drift apart.
/// </summary>
public record PriceBreakdown(
    IReadOnlyList<ResolvedLine> Lines,
    decimal Subtotal,
    IReadOnlyList<AppliedDiscount> Discounts,
    decimal DiscountTotal,
    decimal DeliveryFee,
    string? ShippingMethodName,
    TaxQuote Tax,
    decimal Total,
    decimal GiftCardTotal,
    decimal StoreCreditApplied,
    decimal AmountDue,
    string Currency,
    IReadOnlyList<string> AppliedPromotionIds,
    IReadOnlyList<OrderGiftCardUse> GiftCardUses,
    IReadOnlyList<ShippingQuote> ShippingOptions)
{
    public decimal TotalWeightKg => Lines.Sum(l => l.WeightKg * l.Quantity);
}

/// <summary>
/// §9.43. One redeemable code a shopper could apply right now, for the storefront's
/// available-offers listing — "shown where applicable" rather than a shopper needing to already
/// know a code exists. Only <see cref="Vastora.Domain.Enums.DiscountVisibility.Public"/> Coupons
/// and coded Promotions are ever surfaced this way; a Hidden one only works when typed exactly,
/// which is what makes it usable as an email-only, targeted-campaign code.
/// </summary>
public record AvailableOfferResponse(
    string Source,
    string Code,
    string Label,
    /// <summary>Server-formatted so every client describes "10% off" / "$5 off orders over $50" / "Free shipping" identically.</summary>
    string Summary,
    decimal? MinOrderAmount,
    DateTime? ExpiresAt);

/// <summary>What the caller knows at pricing time. Everything else is looked up.</summary>
public record PricingContext(
    Business Business,
    string? CustomerUserId,
    Address? ShippingAddress,
    string? CouponCode,
    IReadOnlyList<string> PromotionCodes,
    IReadOnlyList<string> GiftCardCodes,
    string? SelectedShippingRateId,
    decimal? ExplicitDeliveryFee,
    bool ApplyStoreCredit,
    /// <summary>
    /// §9.44. <c>Pickup</c>/<c>Digital</c> have no delivery leg at all — no shipping quotes, no
    /// delivery fee, regardless of what a shipping zone or <c>Business.DefaultDeliveryFee</c>
    /// would otherwise charge. Defaults to <c>Delivery</c> so a caller that predates this field
    /// keeps behaving exactly as it did.
    /// </summary>
    FulfillmentMethod FulfillmentMethod = FulfillmentMethod.Delivery);

using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Domain.Entities;

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
    bool ApplyStoreCredit);

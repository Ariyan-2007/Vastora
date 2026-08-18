using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Domain.Enums;

namespace Vastora.Application.Cart;

public record CartItemResponse(
    string ProductId,
    string? VariantId,
    string? VariantSummary,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

/// <summary>
/// The cart, priced. <c>Subtotal</c> alone was the whole answer before §9B, which the Shop
/// blueprint flagged: the storefront could not show a customer their discount without guessing
/// at the coupon rules. Discounts/Tax/EstimatedTotal now come from the same
/// <c>IPricingService</c> checkout uses, so the preview and the charge cannot disagree.
/// </summary>
public record CartResponse(
    string Id,
    string BusinessId,
    List<CartItemResponse> Items,
    string? CouponCode,
    List<string> PromotionCodes,
    decimal Subtotal,
    List<AppliedDiscount> Discounts,
    decimal DiscountTotal,
    decimal EstimatedTotal,
    string Currency,
    int ItemCount,
    /// <summary>Set only for a guest cart (§9.27) — the client must send it back as the cart's identity.</summary>
    string? GuestToken,
    /// <summary>
    /// §9.43. Codes applied on the cart itself, mirroring <see cref="PromotionCodes"/> — was
    /// write-only before this (settable but never priced back into the preview), so a shopper who
    /// applied a gift card saw no discount until they actually checked out.
    /// </summary>
    List<string> GiftCardCodes,
    decimal GiftCardTotal,
    /// <summary>Whether this cart is opted in to spend store credit, and how much it would draw down at this subtotal.</summary>
    bool UseStoreCredit,
    decimal StoreCreditApplied,
    /// <summary>What's actually owed after gift cards and store credit — the number checkout will collect.</summary>
    decimal AmountDue,
    /// <summary>
    /// §9.44. Was entirely absent from this response before — the cart could be discounted but
    /// said nothing about delivery, forcing a guess at what the final charge would be.
    /// <c>Pickup</c>/<c>Digital</c> have no delivery leg: <see cref="DeliveryFee"/> is always 0 and
    /// <see cref="ShippingOptions"/> always empty for those, rather than a misleading "$0 delivery".
    /// Set via <c>PUT /api/shop/cart/fulfillment-method</c>; preview-only, same relationship
    /// <see cref="UseStoreCredit"/> has to <c>CheckoutRequest.UseStoreCredit</c> — repeat the same
    /// choice on <c>CheckoutRequest.FulfillmentMethod</c> for it to actually take effect.
    /// </summary>
    FulfillmentMethod FulfillmentMethod,
    /// <summary>Resolved from the business's shipping zones, or its flat <c>DefaultDeliveryFee</c> when none match — 0 whenever <see cref="FulfillmentMethod"/> has no delivery leg.</summary>
    decimal DeliveryFee,
    string? ShippingMethodName,
    /// <summary>Every rate this cart currently qualifies for — empty until an address narrows it further, and always empty for Pickup/Digital.</summary>
    List<ShippingQuote> ShippingOptions);

public record AddCartItemRequest(string ProductId, int Quantity, string? VariantId = null);

public record UpdateCartItemRequest(int Quantity, string? VariantId = null);

public record ApplyCartCouponRequest(string Code);

public record SetCartStoreCreditRequest(bool UseStoreCredit);

public record SetCartFulfillmentMethodRequest(FulfillmentMethod FulfillmentMethod);

/// <summary>
/// §9.27. Identifies whose cart to act on: an authenticated customer, or an anonymous shopper
/// holding a guest token. Exactly one is populated; the service refuses a request carrying
/// neither.
/// </summary>
public record CartOwner(string? CustomerUserId, string? GuestToken)
{
    public bool IsGuest => string.IsNullOrEmpty(CustomerUserId);

    public static CartOwner ForCustomer(string customerUserId) => new(customerUserId, null);

    public static CartOwner ForGuest(string? guestToken) => new(null, guestToken);
}

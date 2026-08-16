using Vastora.Application.Promotions;

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
    string? GuestToken);

public record AddCartItemRequest(string ProductId, int Quantity, string? VariantId = null);

public record UpdateCartItemRequest(int Quantity, string? VariantId = null);

public record ApplyCartCouponRequest(string Code);

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

using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

public class CartItem
{
    public string ProductId { get; set; } = string.Empty;

    /// <summary>§9.22. Null for a product with no variants; otherwise the specific ProductVariant.Id bought.</summary>
    public string? VariantId { get; set; }

    public string? VariantSummary { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }

    /// <summary>Matching key — the same product in two variants is two distinct lines.</summary>
    public bool Matches(string productId, string? variantId) =>
        ProductId == productId && VariantId == variantId;
}

/// <summary>
/// One active cart per Customer per Business — or per anonymous cart token, for guests (§9.27).
/// Exactly one of CustomerUserId / GuestToken is set; merging on login moves the guest cart's
/// lines into the customer's own.
/// </summary>
public class Cart : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    /// <summary>Empty for a guest cart.</summary>
    public string CustomerUserId { get; set; } = string.Empty;

    /// <summary>§9.27. Opaque token identifying an anonymous shopper's cart. Null once the cart belongs to an account.</summary>
    public string? GuestToken { get; set; }

    public List<CartItem> Items { get; set; } = [];

    public string? CouponCode { get; set; }

    /// <summary>§9.23. Codes for Promotion-backed offers, separate from the single legacy CouponCode.</summary>
    public List<string> PromotionCodes { get; set; } = [];

    /// <summary>
    /// §9.24, §9.43. Gift card codes applied at checkout; settled against RemainingBalance in
    /// order. Was write-only before §9.43 — set here but never read back into pricing, so a
    /// customer who applied a gift card at cart time saw no discount until they actually checked
    /// out. Now threaded into the cart preview the same way <see cref="PromotionCodes"/> is.
    /// </summary>
    public List<string> GiftCardCodes { get; set; } = [];

    /// <summary>§9.24, §9.43. Opt-in to spend store credit against this cart's total, mirroring
    /// <c>CheckoutRequest.UseStoreCredit</c> so the cart preview can show the same discount
    /// checkout will actually apply, rather than the customer discovering it only at the end.</summary>
    public bool UseStoreCredit { get; set; }

    /// <summary>
    /// §9.44. Preview-only, mirroring <c>UseStoreCredit</c>'s relationship to
    /// <c>CheckoutRequest.UseStoreCredit</c>: set here so <c>GET /api/shop/cart</c> can show
    /// whether a delivery fee applies before checkout; <c>CheckoutRequest.FulfillmentMethod</c>
    /// remains the one that's actually charged. <c>Pickup</c>/<c>Digital</c> suppress the delivery
    /// fee and shipping-option list entirely, rather than showing a $0 line that reads as "free
    /// delivery" when delivery was never going to happen at all.
    /// </summary>
    public FulfillmentMethod FulfillmentMethod { get; set; } = FulfillmentMethod.Delivery;

    /// <summary>§9.36. Set once an abandoned-cart nudge has gone out, so one cart yields one nudge.</summary>
    public DateTime? AbandonedReminderSentAt { get; set; }

    /// <summary>Where to reach a guest who abandons — captured at the email step of checkout.</summary>
    public string? ContactEmail { get; set; }
}

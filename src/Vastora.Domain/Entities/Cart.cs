using Vastora.Domain.Common;

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

    /// <summary>§9.24. Gift card codes applied at checkout; settled against RemainingBalance in order.</summary>
    public List<string> GiftCardCodes { get; set; } = [];

    /// <summary>§9.36. Set once an abandoned-cart nudge has gone out, so one cart yields one nudge.</summary>
    public DateTime? AbandonedReminderSentAt { get; set; }

    /// <summary>Where to reach a guest who abandons — captured at the email step of checkout.</summary>
    public string? ContactEmail { get; set; }
}

using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;

    /// <summary>Set when the customer bought a specific variant (§9.22). Null for single-variant products.</summary>
    public string? VariantId { get; set; }

    public string? VariantSummary { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Product.CostPrice snapshotted at checkout — §9.31. Snapshotted for the same reason
    /// UnitPrice is: a later cost change must not retroactively rewrite historical margin.
    /// Null when the product had no recorded cost.
    /// </summary>
    public decimal? UnitCost { get; set; }

    public int Quantity { get; set; }

    /// <summary>How many of this line have been returned/refunded so far (§9.21). Never exceeds Quantity.</summary>
    public int RefundedQuantity { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;

    /// <summary>Null when UnitCost is unknown, so COGS can distinguish "zero cost" from "unrecorded".</summary>
    public decimal? LineCost => UnitCost is null ? null : UnitCost.Value * Quantity;
}

/// <summary>One discount that fired on an order, snapshotted for the invoice.</summary>
public class OrderDiscountLine
{
    public string Source { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

/// <summary>§9.24.</summary>
public class OrderGiftCardUse
{
    public string GiftCardId { get; set; } = string.Empty;
    public string CodeSuffix { get; set; } = string.Empty;
    public decimal AmountApplied { get; set; }
}

public class OrderStatusEvent
{
    public OrderStatus Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = string.Empty;
}

/// <summary>Audit trail for PaymentStatus, parallel to OrderStatusEvent — §9.6. Also what §9.16a's
/// sales ledger reads from to know exactly when an order was marked Paid.</summary>
public class PaymentStatusEvent
{
    public PaymentStatus Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = string.Empty;
}

public class Order : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>Empty for a guest order (§9.27) — GuestEmail identifies the buyer instead.</summary>
    public string CustomerUserId { get; set; } = string.Empty;

    /// <summary>
    /// Contact snapshot. Always populated, for guests and registered customers alike, so an
    /// order remains contactable after the account is deleted or anonymised (§9.37).
    /// </summary>
    public string ContactEmail { get; set; } = string.Empty;

    public string ContactPhone { get; set; } = string.Empty;

    public bool IsGuestOrder { get; set; }

    /// <summary>Opaque lookup token for guest order tracking — there's no account to list it under (§9.27).</summary>
    public string? GuestAccessToken { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    public decimal Subtotal { get; set; }

    public string? CouponCode { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal DeliveryFee { get; set; }

    /// <summary>§9.19. Zero when the Business has no tax configured — not a null, so arithmetic stays total.</summary>
    public decimal TaxAmount { get; set; }

    /// <summary>Snapshotted rate (as a percentage) so a later Business tax-rate change never rewrites history.</summary>
    public decimal TaxRatePercent { get; set; }

    public bool PricesIncludeTax { get; set; }

    public decimal Total { get; set; }

    /// <summary>
    /// Business.Currency snapshotted at checkout — §9.38. Without this, changing a Business's
    /// currency retroactively reinterprets every historical order.
    /// </summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Running total actually refunded (§9.21). Equals Total once fully refunded.</summary>
    public decimal RefundedAmount { get; set; }

    /// <summary>
    /// §9.23. Which Promotions fired on this order. Stored rather than recomputed because
    /// per-customer usage caps are counted from real order history — a promotion edited or
    /// deleted later must not retroactively change how many times a customer has used it.
    /// </summary>
    public List<string> AppliedPromotionIds { get; set; } = [];

    /// <summary>Human-readable breakdown of every discount that fired, for the invoice and the customer's order page.</summary>
    public List<OrderDiscountLine> Discounts { get; set; } = [];

    /// <summary>§9.24. Gift card ids settled against this order, with the amount taken from each.</summary>
    public List<OrderGiftCardUse> GiftCardsUsed { get; set; } = [];

    /// <summary>Store credit spent on this order (§9.24).</summary>
    public decimal StoreCreditApplied { get; set; }

    /// <summary>What the customer still has to pay after gift cards and store credit.</summary>
    public decimal AmountDue { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public Address? ShippingAddress { get; set; }

    public Address? BillingAddress { get; set; }

    public string? DeliveryAgentUserId { get; set; }

    // --- §9.20: fulfillment beyond the in-house delivery agent ---

    public FulfillmentMethod FulfillmentMethod { get; set; } = FulfillmentMethod.Delivery;

    public string? ShippingMethodName { get; set; }

    public string? CarrierName { get; set; }

    public string? TrackingNumber { get; set; }

    public string? TrackingUrl { get; set; }

    /// <summary>Sequential, gapless per Business — assigned on first invoice issue, never reused (§9.33).</summary>
    public string? InvoiceNumber { get; set; }

    public DateTime? InvoicedAt { get; set; }

    /// <summary>Staff-only note, never shown to the customer.</summary>
    public string InternalNote { get; set; } = string.Empty;

    public string CustomerNote { get; set; } = string.Empty;

    public List<OrderStatusEvent> StatusHistory { get; set; } = [];

    public List<PaymentStatusEvent> PaymentStatusHistory { get; set; } = [];

    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
}

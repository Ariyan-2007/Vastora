using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Orders;

public record OrderItemResponse(
    string ProductId,
    string? VariantId,
    string? VariantSummary,
    string ProductName,
    decimal UnitPrice,
    int Quantity,
    int RefundedQuantity,
    decimal LineTotal);

public record OrderStatusEventResponse(OrderStatus Status, DateTime Timestamp, string Note);

public record PaymentStatusEventResponse(PaymentStatus Status, DateTime Timestamp, string Note);

public record OrderDiscountResponse(string Source, string Label, decimal Amount);

public record OrderGiftCardResponse(string CodeSuffix, decimal AmountApplied);

public record OrderResponse(
    string Id,
    string BusinessId,
    string OrderNumber,
    string CustomerUserId,
    bool IsGuestOrder,
    string ContactEmail,
    string ContactPhone,
    List<OrderItemResponse> Items,
    decimal Subtotal,
    string? CouponCode,
    decimal DiscountAmount,
    List<OrderDiscountResponse> Discounts,
    decimal DeliveryFee,
    decimal TaxAmount,
    decimal TaxRatePercent,
    bool PricesIncludeTax,
    decimal Total,
    decimal GiftCardTotal,
    List<OrderGiftCardResponse> GiftCardsUsed,
    decimal StoreCreditApplied,
    decimal AmountDue,
    decimal RefundedAmount,
    string Currency,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    FulfillmentMethod FulfillmentMethod,
    Address? ShippingAddress,
    Address? BillingAddress,
    string? DeliveryAgentUserId,
    string? ShippingMethodName,
    string? CarrierName,
    string? TrackingNumber,
    string? TrackingUrl,
    string? InvoiceNumber,
    string CustomerNote,
    List<OrderStatusEventResponse> StatusHistory,
    List<PaymentStatusEventResponse> PaymentStatusHistory,
    DateTime PlacedAt);

/// <summary>
/// Everything optional is optional on purpose — the pre-§9B contract
/// (<c>{ shippingAddress, deliveryFee }</c>) still works unchanged, and every new field is
/// additive. <paramref name="DeliveryFee"/> overrides the resolved shipping rate (§9.20);
/// <paramref name="ShippingRateId"/> picks one of the quoted rates instead.
/// </summary>
public record CheckoutRequest(
    Address ShippingAddress,
    decimal? DeliveryFee,
    Address? BillingAddress = null,
    string? ShippingRateId = null,
    FulfillmentMethod FulfillmentMethod = FulfillmentMethod.Delivery,
    string? CustomerNote = null,
    bool UseStoreCredit = false,
    List<string>? GiftCardCodes = null,
    /// <summary>Guest checkout only (§9.27) — ignored for an authenticated customer, whose account supplies these.</summary>
    string? GuestEmail = null,
    string? GuestPhone = null,
    string? GuestName = null);

public record UpdateOrderStatusRequest(OrderStatus Status, string Note);

/// <summary>
/// Manual payment recording for the cash-on-delivery flow that's the only one that exists today
/// (§9.6) — there is no payment gateway, so a human (staff, or eventually a webhook once one
/// exists) is the only thing that can mark an order paid.
/// </summary>
public record UpdatePaymentStatusRequest(PaymentStatus Status, string? Note);

public record AssignDeliveryAgentRequest(string DeliveryAgentUserId);

/// <summary>§9.20. Recording an external courier's shipment, for businesses not using the delivery-agent module.</summary>
public record UpdateShipmentRequest(string? CarrierName, string? TrackingNumber, string? TrackingUrl, string? ShippingMethodName);

/// <summary>§9.20/§9.35. Staff-only notes on an order.</summary>
public record UpdateOrderNoteRequest(string InternalNote);

/// <summary>§9.18. Server-side filtering for the BackOffice order list, so it no longer loads every order to filter in the browser.</summary>
public record OrderQuery(
    OrderStatus? Status = null,
    PaymentStatus? PaymentStatus = null,
    string? Search = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>§9.33 — everything a printable invoice needs, computed on demand.</summary>
public record InvoiceResponse(
    string InvoiceNumber,
    DateTime IssuedAt,
    string OrderNumber,
    DateTime OrderPlacedAt,
    string SellerLegalName,
    string SellerAddress,
    string SellerRegistrationNumber,
    string SellerTaxRegistrationNumber,
    string BuyerName,
    string BuyerEmail,
    Address? BillingAddress,
    Address? ShippingAddress,
    List<OrderItemResponse> Items,
    decimal Subtotal,
    List<OrderDiscountResponse> Discounts,
    decimal DiscountTotal,
    decimal DeliveryFee,
    string TaxLabel,
    decimal TaxRatePercent,
    decimal TaxAmount,
    bool PricesIncludeTax,
    decimal Total,
    decimal AmountPaid,
    decimal AmountDue,
    string Currency,
    string FooterNote);

/// <summary>§9.19/§9.20/§9.23 — the cart preview the storefront needs before checkout commits.</summary>
public record CheckoutPreviewResponse(
    decimal Subtotal,
    List<OrderDiscountResponse> Discounts,
    decimal DiscountTotal,
    decimal DeliveryFee,
    string? ShippingMethodName,
    List<ShippingQuote> ShippingOptions,
    decimal TaxAmount,
    List<TaxLine> TaxLines,
    bool PricesIncludeTax,
    decimal Total,
    decimal GiftCardTotal,
    decimal StoreCreditAvailable,
    decimal AmountDue,
    string Currency);

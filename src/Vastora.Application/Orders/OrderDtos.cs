using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Orders;

public record OrderItemResponse(string ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public record OrderStatusEventResponse(OrderStatus Status, DateTime Timestamp, string Note);

public record PaymentStatusEventResponse(PaymentStatus Status, DateTime Timestamp, string Note);

public record OrderResponse(
    string Id,
    string BusinessId,
    string OrderNumber,
    string CustomerUserId,
    List<OrderItemResponse> Items,
    decimal Subtotal,
    string? CouponCode,
    decimal DiscountAmount,
    decimal DeliveryFee,
    decimal Total,
    OrderStatus Status,
    PaymentStatus PaymentStatus,
    Address? ShippingAddress,
    string? DeliveryAgentUserId,
    List<OrderStatusEventResponse> StatusHistory,
    List<PaymentStatusEventResponse> PaymentStatusHistory,
    DateTime PlacedAt);

/// <summary>DeliveryFee is optional (§9.7) — omit it to use Business.DefaultDeliveryFee.</summary>
public record CheckoutRequest(Address ShippingAddress, decimal? DeliveryFee);

public record UpdateOrderStatusRequest(OrderStatus Status, string Note);

/// <summary>
/// Manual payment recording for the cash-on-delivery flow that's the only one that exists today
/// (§9.6) — there is no payment gateway, so a human (staff, or eventually a webhook once one
/// exists) is the only thing that can mark an order paid.
/// </summary>
public record UpdatePaymentStatusRequest(PaymentStatus Status, string? Note);

public record AssignDeliveryAgentRequest(string DeliveryAgentUserId);

using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Orders;

public record OrderItemResponse(string ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

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
    DateTime PlacedAt);

public record CheckoutRequest(Address ShippingAddress, decimal DeliveryFee);

public record UpdateOrderStatusRequest(OrderStatus Status, string Note);

public record AssignDeliveryAgentRequest(string DeliveryAgentUserId);

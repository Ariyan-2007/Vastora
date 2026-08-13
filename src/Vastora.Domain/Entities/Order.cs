using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

public class OrderItem
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal => UnitPrice * Quantity;
}

public class OrderStatusEvent
{
    public OrderStatus Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = string.Empty;
}

public class Order : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string CustomerUserId { get; set; } = string.Empty;

    public List<OrderItem> Items { get; set; } = [];

    public decimal Subtotal { get; set; }

    public string? CouponCode { get; set; }

    public decimal DiscountAmount { get; set; }

    public decimal DeliveryFee { get; set; }

    public decimal Total { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.PendingPayment;

    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    public Address? ShippingAddress { get; set; }

    public string? DeliveryAgentUserId { get; set; }

    public List<OrderStatusEvent> StatusHistory { get; set; } = [];

    public DateTime PlacedAt { get; set; } = DateTime.UtcNow;
}

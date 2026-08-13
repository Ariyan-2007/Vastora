namespace Vastora.Domain.Enums;

public enum OrderStatus
{
    PendingPayment = 1,
    Processing = 2,
    Confirmed = 3,
    OutForDelivery = 4,
    Delivered = 5,
    Cancelled = 6,
    Refunded = 7
}

public enum PaymentStatus
{
    Pending = 1,
    Paid = 2,
    Failed = 3,
    Refunded = 4
}

public enum DeliveryAgentStatus
{
    Free = 1,
    Busy = 2,
    Offline = 3,
    Blocked = 4
}

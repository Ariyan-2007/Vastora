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

/// <summary>How an order reaches the customer — §9.20. Pickup was deferred in §9.14 and lands here.</summary>
public enum FulfillmentMethod
{
    Delivery = 1,
    Pickup = 2,
    /// <summary>Shipped by an external courier; CarrierName/TrackingNumber carry the detail instead of a DeliveryAgent.</summary>
    ExternalCourier = 3,
    /// <summary>Nothing to ship (gift card, download).</summary>
    Digital = 4
}

/// <summary>§9.21. A return request's lifecycle, deliberately separate from OrderStatus.</summary>
public enum ReturnStatus
{
    Requested = 1,
    Approved = 2,
    Rejected = 3,
    /// <summary>Goods physically back with the seller — this is what triggers the restock.</summary>
    Received = 4,
    Refunded = 5,
    Cancelled = 6
}

public enum ReturnResolution
{
    Refund = 1,
    Exchange = 2,
    StoreCredit = 3
}

public enum ReturnReason
{
    Damaged = 1,
    WrongItem = 2,
    NotAsDescribed = 3,
    ChangedMind = 4,
    SizeOrFit = 5,
    Other = 99
}

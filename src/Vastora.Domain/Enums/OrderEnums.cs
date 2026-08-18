namespace Vastora.Domain.Enums;

public enum OrderStatus
{
    PendingPayment = 1,
    Processing = 2,
    Confirmed = 3,
    /// <summary>Delivery/ExternalCourier only — see <see cref="OrderStatusExtensions"/>. A Pickup order never enters this state.</summary>
    OutForDelivery = 4,
    /// <summary>Delivery/ExternalCourier only. The Pickup equivalent is <see cref="PickedUp"/> — see <see cref="OrderStatusExtensions.IsFulfilled"/>.</summary>
    Delivered = 5,
    Cancelled = 6,
    Refunded = 7,
    /// <summary>§9.47. Pickup only — ready and held at the store, waiting on the customer. The Pickup equivalent of <see cref="OutForDelivery"/>.</summary>
    AwaitingPickup = 8,
    /// <summary>§9.47. Pickup only — the customer collected it in person. The Pickup equivalent of <see cref="Delivered"/>.</summary>
    PickedUp = 9
}

/// <summary>
/// §9.47. Before this, every order — Pickup included — moved through
/// <c>Confirmed → OutForDelivery → Delivered</c>, which described a delivery that was never
/// going to happen: no courier, no agent, nothing "out" anywhere. A Pickup order now moves
/// through <c>Confirmed → AwaitingPickup → PickedUp</c> instead (see
/// <c>OrderService</c>'s per-<see cref="FulfillmentMethod"/> transition tables); everywhere else
/// in the codebase that means "the order reached its terminal happy-path state" — revenue
/// recognition, return eligibility, verified-purchase reviews, the post-delivery review-request
/// sweep — treats <see cref="OrderStatus.Delivered"/> and <see cref="OrderStatus.PickedUp"/> as
/// equivalent through this one helper, so a Pickup order gets the same downstream treatment a
/// Delivery order always did instead of silently missing all of it.
/// </summary>
public static class OrderStatusExtensions
{
    public static bool IsFulfilled(this OrderStatus status) =>
        status is OrderStatus.Delivered or OrderStatus.PickedUp;
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
    Cancelled = 6,
    /// <summary>
    /// §9.49. The terminal state for a same-price Exchange — a distinct value from Refunded
    /// rather than reusing it, since no money moved and calling it "Refunded" would misdescribe
    /// what actually happened.
    /// </summary>
    Exchanged = 7
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

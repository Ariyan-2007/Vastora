namespace Vastora.Application.Webhooks;

/// <summary>The event names a tenant may subscribe to — §9.39. Constants rather than an enum so
/// they serialise into payloads and subscription documents as stable strings.</summary>
public static class WebhookEvents
{
    public const string OrderCreated = "order.created";
    public const string OrderStatusChanged = "order.status_changed";
    public const string OrderDelivered = "order.delivered";
    /// <summary>§9.47. The Pickup equivalent of <see cref="OrderDelivered"/> — fired instead of it, never alongside it, when a Pickup order reaches <c>PickedUp</c>.</summary>
    public const string OrderPickedUp = "order.picked_up";
    public const string ProductLowStock = "product.low_stock";
    public const string ReturnRequested = "return.requested";
    public const string ReviewSubmitted = "review.submitted";

    public static readonly string[] All =
    [
        OrderCreated, OrderStatusChanged, OrderDelivered, OrderPickedUp, ProductLowStock, ReturnRequested, ReviewSubmitted
    ];
}

/// <summary>
/// §9.39. Fans a domain event out to every matching tenant subscription.
///
/// Always best-effort, exactly like <c>INotificationService</c> (§9.10): a tenant's broken
/// endpoint must never fail the order that triggered the event. Implementations swallow and log
/// rather than propagate.
/// </summary>
public interface IWebhookPublisher
{
    Task PublishAsync(string tenantId, string businessId, string eventName, object payload, CancellationToken ct = default);
}

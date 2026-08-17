using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.39. A tenant-registered HTTP endpoint that receives platform events. The secret is what
/// lets the receiver verify the payload really came from Vastora (HMAC-SHA256 over the body, in
/// an X-Vastora-Signature header) — without it a webhook URL is an open door for anyone who
/// guesses it.
/// </summary>
public class WebhookSubscription : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Empty means tenant-wide — every Business the tenant owns.</summary>
    public string BusinessId { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    /// <summary>Event names: order.created, order.status_changed, order.delivered, product.low_stock, return.requested.</summary>
    public List<string> Events { get; set; } = [];

    /// <summary>Signing secret, shown once at creation and stored hashed thereafter.</summary>
    public string SecretHash { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public string Description { get; set; } = string.Empty;

    public DateTime? LastDeliveryAt { get; set; }

    public int ConsecutiveFailures { get; set; }

    /// <summary>Auto-disabled after repeated failures so a dead endpoint stops costing delivery attempts.</summary>
    public DateTime? DisabledAt { get; set; }
}

/// <summary>One delivery attempt, kept so a tenant can debug their own integration.</summary>
public class WebhookDelivery : BaseEntity, ITenantScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string SubscriptionId { get; set; } = string.Empty;

    public string EventName { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public int? ResponseStatusCode { get; set; }

    public string? Error { get; set; }

    public int AttemptCount { get; set; }

    public bool Succeeded { get; set; }
}

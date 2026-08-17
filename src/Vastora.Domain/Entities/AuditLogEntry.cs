using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.35. Who changed what, recorded centrally by AuditLogFilter for every mutating request
/// rather than by each service calling a logger — one interception point cannot be forgotten at
/// a new call site, which is exactly how audit trails rot. The trade-off is that entries are
/// HTTP-shaped (route + method + outcome), not domain-shaped (before/after field values); that
/// is enough to answer "who deleted this product and when", which is what the gap actually was.
/// </summary>
public class AuditLogEntry : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string UserEmail { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    /// <summary>Route template rather than the concrete path, so entries group cleanly.</summary>
    public string RouteTemplate { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string IpAddress { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Set only for deletes and status changes — the id of the affected resource.</summary>
    public string? ResourceId { get; set; }

    public long DurationMs { get; set; }
}

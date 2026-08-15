using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// Single-entry, cash-basis bookkeeping — not double-entry/GAAP (§9.16, deliberate simplification).
/// Written automatically by OrderService: Revenue when an order reaches Delivered, Refund when
/// a Delivered order reaches Refunded, DeliveryPayout when an assigned agent is credited. Never
/// written directly by a controller — there is no create/update/delete endpoint for this entity.
/// </summary>
public class LedgerEntry : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public LedgerEntryType Type { get; set; }

    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string ReferenceOrderId { get; set; } = string.Empty;

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>One line of a return — a subset of an OrderItem's quantity.</summary>
public class ReturnItem
{
    public string ProductId { get; set; } = string.Empty;
    public string? VariantId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Unit price snapshotted from the order, so the refund can never exceed what was paid.</summary>
    public decimal UnitPrice { get; set; }

    public decimal LineRefund => UnitPrice * Quantity;
}

public class ReturnStatusEvent
{
    public ReturnStatus Status { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Note { get; set; } = string.Empty;
    public string? ByUserId { get; set; }
}

/// <summary>
/// An RMA — §9.21. Deliberately its own aggregate rather than more OrderStatus values: a return
/// is partial (some lines, some quantities), has its own approval lifecycle, and can outlive the
/// order's own terminal state. Restock happens on → Received (goods physically back), refund
/// bookkeeping on → Refunded; those are separate events and conflating them was the old bug.
/// </summary>
public class ReturnRequest : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string OrderId { get; set; } = string.Empty;

    public string OrderNumber { get; set; } = string.Empty;

    public string RmaNumber { get; set; } = string.Empty;

    public string CustomerUserId { get; set; } = string.Empty;

    public List<ReturnItem> Items { get; set; } = [];

    public ReturnReason Reason { get; set; } = ReturnReason.Other;

    public string ReasonNote { get; set; } = string.Empty;

    public ReturnResolution Resolution { get; set; } = ReturnResolution.Refund;

    public ReturnStatus Status { get; set; } = ReturnStatus.Requested;

    public List<ReturnStatusEvent> StatusHistory { get; set; } = [];

    /// <summary>Sum of the item lines. The delivery fee is deliberately not refunded automatically.</summary>
    public decimal RequestedRefundAmount { get; set; }

    /// <summary>What was actually refunded — staff can settle for less (restocking fee, partial approval).</summary>
    public decimal? ApprovedRefundAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    /// <summary>True once the goods were put back into sellable stock, so a re-received return can't double-restock.</summary>
    public bool Restocked { get; set; }

    public DateTime? RefundedAt { get; set; }
}

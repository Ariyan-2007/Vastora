using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// §9.24. Append-only credit ledger for one customer — the balance is the sum of its entries,
/// never a mutable field, so a concurrent spend can't lose an award. Also the natural settlement
/// route for §9.21 returns resolved as StoreCredit.
/// </summary>
public class StoreCreditEntry : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string CustomerUserId { get; set; } = string.Empty;

    /// <summary>Signed: positive credits the customer, negative spends it.</summary>
    public decimal Amount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public StoreCreditReason Reason { get; set; }

    public string Note { get; set; } = string.Empty;

    public string? ReferenceOrderId { get; set; }

    public string? ReferenceReturnId { get; set; }
}

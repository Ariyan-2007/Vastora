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

    /// <summary>
    /// §9.43. Null means this entry never expires — the natural default for refund-settled credit
    /// (§9.21), which represents money the customer already spent and is owed back indefinitely.
    /// Only meaningful on a credit (positive <see cref="Amount"/>); a promotional grant is the
    /// case that sets it. Computed live the same way <c>Coupon.IsValidNow</c> and
    /// <c>GiftCard.IsRedeemableNow</c> are — no sweep needed, since a filter over the (still
    /// immutable) entries is enough to keep the balance correct at read time.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>Whether this entry still counts toward the live balance — a spend (negative amount) always does; a credit does only until it expires.</summary>
    public bool CountsTowardBalance(DateTime now) => Amount < 0 || ExpiresAt is null || ExpiresAt > now;
}

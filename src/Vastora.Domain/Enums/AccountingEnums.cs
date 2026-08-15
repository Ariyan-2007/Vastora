namespace Vastora.Domain.Enums;

/// <summary>§9.16a — DeliveryPayout added alongside the roadmap's original Revenue/Refund so §9.16d's
/// agent payouts can be attributed to a date range in P&L reports (DeliveryAgentProfile.Balance is
/// only a running total, not per-payout history).</summary>
public enum LedgerEntryType
{
    Revenue = 1,
    Refund = 2,
    DeliveryPayout = 3
}

namespace Vastora.Domain.Enums;

/// <summary>§9.16a — DeliveryPayout added alongside the roadmap's original Revenue/Refund so §9.16d's
/// agent payouts can be attributed to a date range in P&L reports (DeliveryAgentProfile.Balance is
/// only a running total, not per-payout history).</summary>
public enum LedgerEntryType
{
    Revenue = 1,
    Refund = 2,
    DeliveryPayout = 3,
    /// <summary>
    /// §9.31. Written alongside Revenue on the → Delivered transition, from the UnitCost
    /// snapshotted onto each OrderItem at checkout. Its own ledger type (rather than a computed
    /// join back to products) so P&amp;L date-windowing works on it exactly like every other line,
    /// and so a later cost-price edit can never rewrite a closed period.
    /// </summary>
    CostOfGoodsSold = 4,
    /// <summary>Tax collected on a delivered order — a liability, deliberately not revenue (§9.19).</summary>
    TaxCollected = 5
}

/// <summary>Where a shopper-visible content block appears on a storefront — §9.30.</summary>
public enum ContentBlockType
{
    /// <summary>Homepage hero/carousel slide.</summary>
    Banner = 1,
    /// <summary>Standalone page reachable by slug: about, contact, terms, privacy, shipping policy.</summary>
    Page = 2,
    /// <summary>Navigation menu entry.</summary>
    MenuItem = 3,
    /// <summary>Blog/news article.</summary>
    Article = 4
}

/// <summary>§9.24. A store-credit ledger is append-only; balance is the sum of its entries.</summary>
public enum StoreCreditReason
{
    GiftCardRedemption = 1,
    RefundToCredit = 2,
    LoyaltyReward = 3,
    ManualAdjustment = 4,
    Spent = 5
}

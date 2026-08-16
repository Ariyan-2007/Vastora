namespace Vastora.Application.Accounting;

public record ExpenseResponse(
    string Id, string BusinessId, string Category, decimal Amount, string Note, DateTime IncurredAt, string CreatedByUserId);

public record CreateExpenseRequest(string Category, decimal Amount, string Note, DateTime IncurredAt);

public record UpdateExpenseRequest(string Category, decimal Amount, string Note, DateTime IncurredAt);

/// <summary>
/// §9.16c, corrected by §9.31. Two profit lines now, because they answer different questions:
///
/// <c>GrossProfit = Revenue − Refunds − CostOfGoodsSold</c> — the margin on what was actually
/// sold, and the number most retailers manage on day to day. It could not be produced at all
/// before <c>Product.CostPrice</c> existed.
///
/// <c>NetProfit = GrossProfit − Expenses − DeliveryPayouts</c> — what's left after running the
/// business. This used to be computed without COGS at all, which inflated it by the entire cost
/// of every item sold unless the merchant happened to log each purchase as a manual Expense in
/// the same window it sold in.
///
/// Revenue here is net of tax: tax collected is money owed onward, not earnings, and is reported
/// separately as <paramref name="TaxCollected"/>.
/// <paramref name="UncostedOrderCount"/> counts delivered orders in the window with no recorded
/// cost on any line — the honest measure of how much the COGS figure is understating.
/// Single-entry, cash-basis — not a GAAP-compliant statement.
/// </summary>
public record ProfitAndLossResponse(
    DateTime From,
    DateTime To,
    decimal Revenue,
    decimal Refunds,
    decimal CostOfGoodsSold,
    decimal GrossProfit,
    decimal GrossMarginPercent,
    decimal Expenses,
    decimal DeliveryPayouts,
    decimal NetProfit,
    decimal TaxCollected,
    int UncostedOrderCount);

/// <summary>
/// §9.16c, corrected by §9.31. A snapshot, not a period.
///
/// <paramref name="InventoryValueAtCost"/> replaces what this used to report: inventory valued at
/// *retail* price, which overstated assets by the entire unrealised margin. Retail value is still
/// surfaced separately because merchandising wants it — it just isn't an asset figure.
///
/// Liabilities now exist, if only partially: tax collected and not yet remitted, and outstanding
/// gift-card float. Both are real obligations that were previously counted as if they were the
/// business's own money. Still incomplete — supplier payables and accruals are not tracked.
/// </summary>
public record BalanceSheetResponse(
    decimal CashPosition,
    decimal InventoryValueAtCost,
    decimal InventoryValueAtRetail,
    decimal TotalAssets,
    decimal TaxPayable,
    decimal GiftCardLiability,
    decimal TotalLiabilities,
    decimal NetPosition);

/// <summary>§9.32. The per-business dashboard a BusinessAdmin had no endpoint for at all.</summary>
public record SalesPointResponse(DateTime Date, decimal Revenue, int OrderCount);

public record TopProductResponse(string ProductId, string ProductName, int QuantitySold, decimal Revenue);

public record OrderStatusCountResponse(string Status, int Count);

public record BusinessDashboardResponse(
    DateTime From,
    DateTime To,
    decimal Revenue,
    decimal GrossProfit,
    int OrderCount,
    int DeliveredCount,
    int CancelledCount,
    decimal AverageOrderValue,
    int UniqueCustomers,
    int RepeatCustomers,
    decimal RepeatCustomerRate,
    int NewCustomers,
    int PendingReturns,
    int LowStockCount,
    List<SalesPointResponse> DailySales,
    List<TopProductResponse> TopProducts,
    List<OrderStatusCountResponse> StatusBreakdown,
    string Currency);

namespace Vastora.Application.Analytics;

public record BusinessAnalyticsEntry(string BusinessId, string BusinessName, int OrderCount, decimal Revenue);

public record TopProductStat(string ProductId, string ProductName, int QuantitySold, decimal Revenue);

/// <summary>
/// Cross-business rollup for a Tenant's SuperOffice (§9.8). "Revenue" is recognized on
/// Status == Delivered only (never at order placement) — same recognition rule §9.16a's sales
/// ledger uses, so this dashboard number and the accounting module's number never disagree.
/// "OrderCount" is broader: every non-Cancelled order, since a still-Processing order is real
/// pipeline activity worth seeing even before it's counted as revenue.
/// </summary>
public record TenantAnalyticsResponse(
    decimal TotalRevenue,
    int TotalOrders,
    List<BusinessAnalyticsEntry> Businesses,
    List<TopProductStat> TopProducts);

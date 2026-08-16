using Vastora.Domain.Enums;

namespace Vastora.Application.Inventory;

public record StockMovementResponse(
    string Id,
    string ProductId,
    string? VariantId,
    StockMovementType Type,
    int QuantityDelta,
    string Reason,
    string? ReferenceOrderId,
    string? CreatedByUserId,
    DateTime CreatedAt);

public record LowStockProductResponse(
    string ProductId,
    string ProductName,
    string Sku,
    int StockQuantity,
    int ReorderThreshold,
    int? ReorderQuantity);

public record CategoryValuationEntry(string CategoryId, decimal ValueAtCost, decimal ValueAtRetail);

/// <summary>
/// §9.31. <paramref name="TotalValueAtCost"/> is the accounting figure — what the stock actually
/// cost to acquire, and the only defensible number for a balance sheet.
/// <paramref name="TotalValueAtRetail"/> is what it would sell for; this endpoint used to return
/// *only* that, labelled as the inventory asset, which overstated assets by the entire unrealised
/// margin. <paramref name="UnvaluedProductCount"/> is how many products have no recorded
/// CostPrice and are therefore missing from the cost figure — surfaced rather than silently
/// treated as free.
/// </summary>
public record InventoryValuationResponse(
    decimal TotalValueAtCost,
    decimal TotalValueAtRetail,
    decimal PotentialMargin,
    int UnvaluedProductCount,
    List<CategoryValuationEntry> ByCategory);

/// <summary>Type must be Restock, Adjustment or DamageWriteOff — Sale/Return are system-generated only (§9.15c).</summary>
public record AdjustStockRequest(int QuantityDelta, string Reason, StockMovementType Type, string? VariantId = null);

using Vastora.Domain.Enums;

namespace Vastora.Application.Inventory;

public record StockMovementResponse(
    string Id,
    string ProductId,
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

public record CategoryValuationEntry(string CategoryId, decimal Value);

public record InventoryValuationResponse(decimal TotalValue, List<CategoryValuationEntry> ByCategory);

/// <summary>Type must be Restock, Adjustment or DamageWriteOff — Sale/Return are system-generated only (§9.15c).</summary>
public record AdjustStockRequest(int QuantityDelta, string Reason, StockMovementType Type);

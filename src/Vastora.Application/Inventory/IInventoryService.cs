using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.Application.Inventory;

public interface IInventoryService
{
    /// <summary>
    /// The single place Product.StockQuantity is allowed to change (§9.15a) — updates the field
    /// and writes the audit entry together. QuantityDelta is signed (negative removes stock).
    /// </summary>
    Task RecordMovementAsync(
        string tenantId, string businessId, string productId, StockMovementType type, int quantityDelta,
        string reason, string? referenceOrderId = null, string? createdByUserId = null, CancellationToken ct = default);

    Task<List<StockMovementResponse>> GetMovementsForProductAsync(string businessId, string productId, CancellationToken ct = default);

    /// <summary>Every tracked product with a configured ReorderThreshold at or below its current stock — §9.15b.</summary>
    Task<List<LowStockProductResponse>> GetLowStockAsync(string businessId, CancellationToken ct = default);

    /// <summary>Σ(StockQuantity × Price), overall and per category — §9.15d. Feeds §9.16c's balance sheet.</summary>
    Task<InventoryValuationResponse> GetValuationAsync(string businessId, CancellationToken ct = default);

    /// <summary>Manual recount/damage/restock correction (§9.15c) — the replacement for the old silent overwrite in ProductService.UpdateAsync.</summary>
    Task<ProductResponse> AdjustStockAsync(string tenantId, string businessId, string productId, AdjustStockRequest request, string? createdByUserId, CancellationToken ct = default);
}

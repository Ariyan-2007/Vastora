using Vastora.Application.Common;
using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.Application.Inventory;

public interface IInventoryService
{
    /// <summary>
    /// The single place Product stock is allowed to change (§9.15a) — updates the balance and
    /// writes the audit entry together. QuantityDelta is signed (negative removes stock).
    ///
    /// The adjustment is atomic and refuses to take stock below zero (§9.17). A negative delta
    /// that would overdraw throws <c>ConflictException</c> and writes no movement, so the ledger
    /// can never disagree with the balance.
    /// </summary>
    Task RecordMovementAsync(
        string tenantId, string businessId, string productId, StockMovementType type, int quantityDelta,
        string reason, string? referenceOrderId = null, string? createdByUserId = null,
        string? variantId = null, CancellationToken ct = default);

    /// <summary>
    /// Checkout's stock path (§9.17). Same guarantees as <see cref="RecordMovementAsync"/> but
    /// returns false instead of throwing when stock is short, because checkout has already
    /// deducted earlier lines by the time it finds out and needs to compensate rather than
    /// unwind through an exception.
    /// </summary>
    Task<bool> TryConsumeStockAsync(
        string tenantId, string businessId, string productId, string? variantId, int quantity,
        string reason, string referenceOrderId, string? createdByUserId, CancellationToken ct = default);

    Task<PagedResult<StockMovementResponse>> GetMovementsForProductAsync(
        string businessId, string productId, PageRequest page, CancellationToken ct = default);

    /// <summary>Every tracked product with a configured ReorderThreshold at or below its current stock — §9.15b.</summary>
    Task<List<LowStockProductResponse>> GetLowStockAsync(string businessId, CancellationToken ct = default);

    /// <summary>
    /// Inventory valued both ways — §9.15d, corrected by §9.31. <c>TotalValueAtCost</c> is the
    /// figure the balance sheet uses; <c>TotalValueAtRetail</c> is what this endpoint used to
    /// return *as if it were cost*, kept only because merchandising genuinely wants to see it.
    /// </summary>
    Task<InventoryValuationResponse> GetValuationAsync(string businessId, CancellationToken ct = default);

    /// <summary>Manual recount/damage/restock correction (§9.15c) — the replacement for the old silent overwrite in ProductService.UpdateAsync.</summary>
    Task<ProductResponse> AdjustStockAsync(string tenantId, string businessId, string productId, AdjustStockRequest request, string? createdByUserId, CancellationToken ct = default);
}

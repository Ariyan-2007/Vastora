using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Products;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Inventory;

public class InventoryService(
    IMongoRepository<Product> products,
    IMongoRepository<StockMovement> movements,
    IProductStockStore stockStore,
    IProductService productService) : IInventoryService
{
    public async Task RecordMovementAsync(
        string tenantId, string businessId, string productId, StockMovementType type, int quantityDelta,
        string reason, string? referenceOrderId = null, string? createdByUserId = null,
        string? variantId = null, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        var result = await stockStore.TryAdjustAsync(productId, variantId, quantityDelta, ct);
        if (!result.Applied)
        {
            throw new ConflictException(
                $"'{product.Name}' only has {result.AvailableQuantity} in stock — cannot apply a change of {quantityDelta}.");
        }

        await WriteMovementAsync(tenantId, businessId, productId, variantId, type, quantityDelta, reason, referenceOrderId, createdByUserId, ct);
    }

    public async Task<bool> TryConsumeStockAsync(
        string tenantId, string businessId, string productId, string? variantId, int quantity,
        string reason, string referenceOrderId, string? createdByUserId, CancellationToken ct = default)
    {
        var result = await stockStore.TryAdjustAsync(productId, variantId, -quantity, ct);
        if (!result.Applied)
        {
            return false;
        }

        await WriteMovementAsync(tenantId, businessId, productId, variantId, StockMovementType.Sale, -quantity,
            reason, referenceOrderId, createdByUserId, ct);
        return true;
    }

    private async Task WriteMovementAsync(
        string tenantId, string businessId, string productId, string? variantId, StockMovementType type,
        int quantityDelta, string reason, string? referenceOrderId, string? createdByUserId, CancellationToken ct) =>
        await movements.AddAsync(new StockMovement
        {
            TenantId = tenantId,
            BusinessId = businessId,
            ProductId = productId,
            VariantId = variantId,
            Type = type,
            QuantityDelta = quantityDelta,
            Reason = reason,
            ReferenceOrderId = referenceOrderId,
            CreatedByUserId = createdByUserId
        }, ct);

    public async Task<PagedResult<StockMovementResponse>> GetMovementsForProductAsync(
        string businessId, string productId, PageRequest page, CancellationToken ct = default)
    {
        var result = await movements.FindPagedAsync(
            m => m.BusinessId == businessId && m.ProductId == productId, page, m => m.CreatedAt, ct: ct);

        return result.Map(m => new StockMovementResponse(
            m.Id, m.ProductId, m.VariantId, m.Type, m.QuantityDelta, m.Reason, m.ReferenceOrderId, m.CreatedByUserId, m.CreatedAt));
    }

    public async Task<List<LowStockProductResponse>> GetLowStockAsync(string businessId, CancellationToken ct = default)
    {
        var list = await products.FindAsync(p => p.BusinessId == businessId && p.TrackInventory && p.ReorderThreshold != null, ct);
        return list
            .Where(p => p.StockQuantity <= p.ReorderThreshold!.Value)
            .Select(p => new LowStockProductResponse(p.Id, p.Name, p.Sku, p.StockQuantity, p.ReorderThreshold!.Value, p.ReorderQuantity))
            .ToList();
    }

    public async Task<InventoryValuationResponse> GetValuationAsync(string businessId, CancellationToken ct = default)
    {
        var list = await products.FindAsync(p => p.BusinessId == businessId, ct);

        // §9.31: a product with no recorded CostPrice contributes nothing to the cost figure and
        // is counted separately, rather than being valued at zero — "we don't know" and "it was
        // free" are different facts and a balance sheet must not conflate them.
        var atCost = list.Sum(p => p.StockQuantity * (p.CostPrice ?? 0m));
        var atRetail = list.Sum(p => p.StockQuantity * p.Price);
        var unvalued = list.Count(p => p.CostPrice is null && p.StockQuantity > 0);

        var byCategory = list
            .GroupBy(p => p.CategoryId)
            .Select(g => new CategoryValuationEntry(
                g.Key,
                Round(g.Sum(p => p.StockQuantity * (p.CostPrice ?? 0m))),
                Round(g.Sum(p => p.StockQuantity * p.Price))))
            .ToList();

        return new InventoryValuationResponse(
            Round(atCost), Round(atRetail), Round(atRetail - atCost), unvalued, byCategory);
    }

    public async Task<ProductResponse> AdjustStockAsync(string tenantId, string businessId, string productId, AdjustStockRequest request, string? createdByUserId, CancellationToken ct = default)
    {
        if (request.Type is StockMovementType.Sale or StockMovementType.Return)
        {
            throw new ForbiddenException("Sale/Return movements are system-generated only — use Restock, Adjustment or DamageWriteOff here.");
        }

        await RecordMovementAsync(tenantId, businessId, productId, request.Type, request.QuantityDelta,
            request.Reason, null, createdByUserId, request.VariantId, ct);

        return await productService.GetByIdAsync(businessId, productId, ct);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

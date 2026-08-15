using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Products;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Inventory;

public class InventoryService(
    IMongoRepository<Product> products,
    IMongoRepository<StockMovement> movements,
    IProductService productService) : IInventoryService
{
    public async Task RecordMovementAsync(
        string tenantId, string businessId, string productId, StockMovementType type, int quantityDelta,
        string reason, string? referenceOrderId = null, string? createdByUserId = null, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        product.StockQuantity += quantityDelta;
        product.UpdatedAt = DateTime.UtcNow;
        await products.UpdateAsync(product, ct);

        await movements.AddAsync(new StockMovement
        {
            TenantId = tenantId,
            BusinessId = businessId,
            ProductId = productId,
            Type = type,
            QuantityDelta = quantityDelta,
            Reason = reason,
            ReferenceOrderId = referenceOrderId,
            CreatedByUserId = createdByUserId
        }, ct);
    }

    public async Task<List<StockMovementResponse>> GetMovementsForProductAsync(string businessId, string productId, CancellationToken ct = default)
    {
        var list = await movements.FindAsync(m => m.BusinessId == businessId && m.ProductId == productId, ct);
        return list.OrderByDescending(m => m.CreatedAt)
            .Select(m => new StockMovementResponse(m.Id, m.ProductId, m.Type, m.QuantityDelta, m.Reason, m.ReferenceOrderId, m.CreatedByUserId, m.CreatedAt))
            .ToList();
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
        var totalValue = list.Sum(p => p.StockQuantity * p.Price);
        var byCategory = list
            .GroupBy(p => p.CategoryId)
            .Select(g => new CategoryValuationEntry(g.Key, g.Sum(p => p.StockQuantity * p.Price)))
            .ToList();

        return new InventoryValuationResponse(totalValue, byCategory);
    }

    public async Task<ProductResponse> AdjustStockAsync(string tenantId, string businessId, string productId, AdjustStockRequest request, string? createdByUserId, CancellationToken ct = default)
    {
        if (request.Type is StockMovementType.Sale or StockMovementType.Return)
        {
            throw new ForbiddenException("Sale/Return movements are system-generated only — use Restock, Adjustment or DamageWriteOff here.");
        }

        await RecordMovementAsync(tenantId, businessId, productId, request.Type, request.QuantityDelta, request.Reason, null, createdByUserId, ct);
        return await productService.GetByIdAsync(businessId, productId, ct);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Inventory;
using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>Stock movement history, low-stock alerts, manual adjustments, and inventory valuation — §9.15.</summary>
[Tags("BackOffice - Inventory")]
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class InventoryController(ICurrentUserContext currentUser, IInventoryService inventoryService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet("products/{productId}/stock-movements")]
    public async Task<ActionResult<PagedResult<StockMovementResponse>>> GetMovements(
        string businessId, string productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        var result = await inventoryService.GetMovementsForProductAsync(businessId, productId, PageRequest.Of(page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost("products/{productId}/stock-adjustments")]
    public async Task<ActionResult<ProductResponse>> AdjustStock(string businessId, string productId, AdjustStockRequest request, CancellationToken ct)
    {
        var result = await inventoryService.AdjustStockAsync(ResolvedTenantId, businessId, productId, request, CurrentUser.UserId, ct);
        return Ok(result);
    }

    [HttpGet("inventory/low-stock")]
    public async Task<ActionResult<List<LowStockProductResponse>>> GetLowStock(string businessId, CancellationToken ct)
    {
        var result = await inventoryService.GetLowStockAsync(businessId, ct);
        return Ok(result);
    }

    [HttpGet("inventory/valuation")]
    public async Task<ActionResult<InventoryValuationResponse>> GetValuation(string businessId, CancellationToken ct)
    {
        var result = await inventoryService.GetValuationAsync(businessId, ct);
        return Ok(result);
    }
}

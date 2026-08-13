using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Route("api/businesses/{businessId}/products")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
public class ProductsController(ICurrentUserContext currentUser, IProductService productService, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<ProductResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await productService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpGet("{productId}")]
    public async Task<ActionResult<ProductResponse>> GetById(string businessId, string productId, CancellationToken ct)
    {
        await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await productService.GetByIdAsync(businessId, productId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(string businessId, CreateProductRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await productService.CreateAsync(tenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPut("{productId}")]
    public async Task<ActionResult<ProductResponse>> Update(string businessId, string productId, UpdateProductRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await productService.UpdateAsync(tenantId, businessId, productId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{productId}/status")]
    public async Task<ActionResult<ProductResponse>> UpdateStatus(string businessId, string productId, [FromBody] ProductStatus status, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await productService.UpdateStatusAsync(tenantId, businessId, productId, status, ct);
        return Ok(result);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> Delete(string businessId, string productId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        await productService.DeleteAsync(tenantId, businessId, productId, ct);
        return NoContent();
    }
}

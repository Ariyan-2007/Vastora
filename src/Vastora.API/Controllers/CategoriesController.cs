using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Categories;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Route("api/businesses/{businessId}/categories")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
public class CategoriesController(ICurrentUserContext currentUser, ICategoryService categoryService, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await categoryService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpGet("{categoryId}")]
    public async Task<ActionResult<CategoryResponse>> GetById(string businessId, string categoryId, CancellationToken ct)
    {
        await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await categoryService.GetByIdAsync(businessId, categoryId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(string businessId, CreateCategoryRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await categoryService.CreateAsync(tenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPut("{categoryId}")]
    public async Task<ActionResult<CategoryResponse>> Update(string businessId, string categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        var result = await categoryService.UpdateAsync(tenantId, businessId, categoryId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{categoryId}")]
    public async Task<IActionResult> Delete(string businessId, string categoryId, CancellationToken ct)
    {
        var tenantId = await EnsureBusinessAccessAsync(businessId, businessService);
        await categoryService.DeleteAsync(tenantId, businessId, categoryId, ct);
        return NoContent();
    }
}

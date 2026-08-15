using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Categories;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Tags("BackOffice - Categories")]
[Route("api/businesses/{businessId}/categories")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class CategoriesController(ICurrentUserContext currentUser, ICategoryService categoryService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<CategoryResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        var result = await categoryService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    /// <summary>Same Categories as GetAll, nested by ParentCategoryId — §9.5.</summary>
    [HttpGet("tree")]
    public async Task<ActionResult<List<CategoryTreeNode>>> GetTree(string businessId, CancellationToken ct)
    {
        var result = await categoryService.GetTreeAsync(businessId, ct);
        return Ok(result);
    }

    [HttpGet("{categoryId}")]
    public async Task<ActionResult<CategoryResponse>> GetById(string businessId, string categoryId, CancellationToken ct)
    {
        var result = await categoryService.GetByIdAsync(businessId, categoryId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CategoryResponse>> Create(string businessId, CreateCategoryRequest request, CancellationToken ct)
    {
        var result = await categoryService.CreateAsync(ResolvedTenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPut("{categoryId}")]
    public async Task<ActionResult<CategoryResponse>> Update(string businessId, string categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        var result = await categoryService.UpdateAsync(ResolvedTenantId, businessId, categoryId, request, ct);
        return Ok(result);
    }

    [HttpDelete("{categoryId}")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<IActionResult> Delete(string businessId, string categoryId, CancellationToken ct)
    {
        await categoryService.DeleteAsync(ResolvedTenantId, businessId, categoryId, ct);
        return NoContent();
    }
}

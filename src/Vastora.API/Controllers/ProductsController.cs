using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

[Tags("BackOffice - Products")]
[Route("api/businesses/{businessId}/products")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class ProductsController(ICurrentUserContext currentUser, IProductService productService, IFileStorageService fileStorage)
    : VastoraControllerBase(currentUser)
{
    private static readonly Dictionary<string, string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["image/gif"] = ".gif"
    };

    [HttpGet]
    public async Task<ActionResult<List<ProductResponse>>> GetAll(string businessId, CancellationToken ct)
    {
        var result = await productService.GetForBusinessAsync(businessId, ct);
        return Ok(result);
    }

    [HttpGet("{productId}")]
    public async Task<ActionResult<ProductResponse>> GetById(string businessId, string productId, CancellationToken ct)
    {
        var result = await productService.GetByIdAsync(businessId, productId, ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> Create(string businessId, CreateProductRequest request, CancellationToken ct)
    {
        var result = await productService.CreateAsync(ResolvedTenantId, businessId, request, ct);
        return Ok(result);
    }

    [HttpPut("{productId}")]
    public async Task<ActionResult<ProductResponse>> Update(string businessId, string productId, UpdateProductRequest request, CancellationToken ct)
    {
        var result = await productService.UpdateAsync(ResolvedTenantId, businessId, productId, request, ct);
        return Ok(result);
    }

    [HttpPatch("{productId}/status")]
    public async Task<ActionResult<ProductResponse>> UpdateStatus(string businessId, string productId, [FromBody] ProductStatus status, CancellationToken ct)
    {
        var result = await productService.UpdateStatusAsync(ResolvedTenantId, businessId, productId, status, ct);
        return Ok(result);
    }

    [HttpDelete("{productId}")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<IActionResult> Delete(string businessId, string productId, CancellationToken ct)
    {
        await productService.DeleteAsync(ResolvedTenantId, businessId, productId, ct);
        return NoContent();
    }

    /// <summary>Uploads one image, appends it to Product.Images (§9.5). Local disk storage — see IFileStorageService.</summary>
    [HttpPost("{productId}/images")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<ProductResponse>> UploadImage(string businessId, string productId, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return BadRequest("File is empty.");
        }

        if (!AllowedImageContentTypes.TryGetValue(file.ContentType, out var extension))
        {
            return BadRequest("Unsupported image type. Allowed: image/jpeg, image/png, image/webp, image/gif.");
        }

        await using var stream = file.OpenReadStream();
        var url = await fileStorage.SaveAsync(businessId, stream, extension, ct);
        var result = await productService.AddImageAsync(ResolvedTenantId, businessId, productId, url, ct);
        return Ok(result);
    }
}

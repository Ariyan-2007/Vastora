using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Categories;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Products;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>Public storefront browsing — the Shop. No authentication required.</summary>
[Tags("Shop - Catalog")]
[Route("api/shop/{businessSlug}")]
[AllowAnonymous]
public class ShopCatalogController(
    ICurrentUserContext currentUser,
    IBusinessService businessService,
    ICategoryService categoryService,
    IProductService productService) : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<BusinessResponse>> GetStorefront(string businessSlug, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        return Ok(business);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<List<CategoryResponse>>> GetCategories(string businessSlug, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var categories = await categoryService.GetForBusinessAsync(business.Id, ct);
        return Ok(categories.Where(c => c.IsActive).ToList());
    }

    [HttpGet("products")]
    public async Task<ActionResult<List<ProductResponse>>> GetProducts(
        string businessSlug, [FromQuery] string? categoryId, [FromQuery] string? search, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var products = await productService.GetPublicCatalogAsync(business.Id, categoryId, search, ct);
        return Ok(products);
    }

    [HttpGet("products/{productId}")]
    public async Task<ActionResult<ProductResponse>> GetProduct(string businessSlug, string productId, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var product = await productService.GetByIdAsync(business.Id, productId, ct);
        if (product.Status != ProductStatus.Active)
        {
            return NotFound();
        }

        return Ok(product);
    }
}

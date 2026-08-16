using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Categories;
using Vastora.Application.Common;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Content;
using Vastora.Application.Products;
using Vastora.Application.Reviews;
using Vastora.Application.Wishlists;
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
    IProductService productService,
    IReviewService reviewService,
    IWishlistService wishlistService,
    IContentService contentService) : VastoraControllerBase(currentUser)
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

    /// <summary>§9.29. Faceted, sorted and paged. Cost price is stripped from this projection.</summary>
    [HttpGet("products")]
    public async Task<ActionResult<PagedResult<ProductResponse>>> GetProducts(
        string businessSlug, [FromQuery] CatalogQuery query, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var products = await productService.GetPublicCatalogAsync(business.Id, query, ct);
        return Ok(products);
    }

    /// <summary>The filter values the storefront's sidebar offers, counted over the same query as the listing.</summary>
    [HttpGet("products/facets")]
    public async Task<ActionResult<CatalogFacetsResponse>> GetFacets(
        string businessSlug, [FromQuery] CatalogQuery query, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var facets = await productService.GetCatalogFacetsAsync(business.Id, query, ct);
        return Ok(facets);
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

    // --- §9.25: public review reads ---

    [HttpGet("products/{productId}/reviews")]
    public async Task<ActionResult<PagedResult<ReviewResponse>>> GetReviews(
        string businessSlug, string productId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var reviews = await reviewService.GetPublishedForProductAsync(business.Id, productId, PageRequest.Of(page, pageSize), ct);
        return Ok(reviews);
    }

    [HttpGet("products/{productId}/reviews/summary")]
    public async Task<ActionResult<ReviewSummaryResponse>> GetReviewSummary(string businessSlug, string productId, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var summary = await reviewService.GetSummaryAsync(business.Id, productId, ct);
        return Ok(summary);
    }

    [HttpPost("reviews/{reviewId}/helpful")]
    public async Task<ActionResult<ReviewResponse>> MarkHelpful(string businessSlug, string reviewId, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await reviewService.MarkHelpfulAsync(business.Id, reviewId, ct);
        return Ok(result);
    }

    // --- §9.26: recommendations ---

    [HttpGet("products/{productId}/also-bought")]
    public async Task<ActionResult<List<RecommendedProductResponse>>> GetAlsoBought(string businessSlug, string productId, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await wishlistService.GetAlsoBoughtAsync(business.Id, productId, ct: ct);
        return Ok(result);
    }

    [HttpGet("products/{productId}/related")]
    public async Task<ActionResult<List<RecommendedProductResponse>>> GetRelated(string businessSlug, string productId, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await wishlistService.GetRelatedAsync(business.Id, productId, ct: ct);
        return Ok(result);
    }

    // --- §9.30: storefront content ---

    [HttpGet("banners")]
    public async Task<ActionResult<List<ContentBlockResponse>>> GetBanners(string businessSlug, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await contentService.GetPublicAsync(business.Id, ContentBlockType.Banner, ct);
        return Ok(result);
    }

    [HttpGet("menu")]
    public async Task<ActionResult<List<MenuNode>>> GetMenu(string businessSlug, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await contentService.GetMenuAsync(business.Id, ct);
        return Ok(result);
    }

    /// <summary>Static pages — About, Contact, Terms, Privacy Policy. The last two are legally required in most markets.</summary>
    [HttpGet("pages")]
    public async Task<ActionResult<List<ContentBlockResponse>>> GetPages(string businessSlug, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await contentService.GetPublicAsync(business.Id, ContentBlockType.Page, ct);
        return Ok(result);
    }

    [HttpGet("pages/{slug}")]
    public async Task<ActionResult<ContentBlockResponse>> GetPage(string businessSlug, string slug, CancellationToken ct)
    {
        var business = await businessService.GetPublicBySlugAsync(businessSlug, ct);
        var result = await contentService.GetPublicBySlugAsync(business.Id, slug, ct);
        return Ok(result);
    }
}

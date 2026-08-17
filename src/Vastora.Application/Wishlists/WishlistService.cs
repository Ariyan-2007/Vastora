using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Wishlists;

public record WishlistItemResponse(
    string Id,
    string ProductId,
    string ProductName,
    string Slug,
    decimal Price,
    decimal EffectivePrice,
    string? ImageUrl,
    bool InStock,
    DateTime AddedAt);

public record RecommendedProductResponse(string ProductId, string ProductName, string Slug, decimal EffectivePrice, string? ImageUrl, int TimesBoughtTogether);

/// <summary>§9.26.</summary>
public interface IWishlistService
{
    Task<PagedResult<WishlistItemResponse>> GetAsync(string businessId, string customerUserId, PageRequest page, CancellationToken ct = default);

    Task<WishlistItemResponse> AddAsync(string tenantId, string businessId, string customerUserId, string productId, CancellationToken ct = default);

    Task RemoveAsync(string businessId, string customerUserId, string productId, CancellationToken ct = default);

    /// <summary>
    /// "Customers who bought this also bought" — computed from real order history rather than a
    /// curated related-products list, so it needs no data model of its own and improves on its own
    /// as orders accumulate.
    /// </summary>
    Task<List<RecommendedProductResponse>> GetAlsoBoughtAsync(string businessId, string productId, int limit = 8, CancellationToken ct = default);

    /// <summary>Same category, excluding the product itself — the fallback when there's no order history yet.</summary>
    Task<List<RecommendedProductResponse>> GetRelatedAsync(string businessId, string productId, int limit = 8, CancellationToken ct = default);
}

/// <inheritdoc cref="IWishlistService"/>
public class WishlistService(
    IMongoRepository<WishlistItem> wishlist,
    IMongoRepository<Product> products,
    IMongoRepository<Order> orders) : IWishlistService
{
    public async Task<PagedResult<WishlistItemResponse>> GetAsync(string businessId, string customerUserId, PageRequest page, CancellationToken ct = default)
    {
        var result = await wishlist.FindPagedAsync(
            w => w.BusinessId == businessId && w.CustomerUserId == customerUserId, page, w => w.CreatedAt, ct: ct);

        var items = new List<WishlistItemResponse>();
        foreach (var entry in result.Items)
        {
            var product = await products.GetByIdAsync(entry.ProductId, ct);
            if (product is null)
            {
                // The product was deleted after being saved. Skipped rather than surfaced as a
                // broken row — the wishlist is a convenience, not a record.
                continue;
            }

            items.Add(new WishlistItemResponse(
                entry.Id, product.Id, product.Name, product.Slug, product.Price, product.EffectivePrice,
                product.Images.FirstOrDefault(), product.StockQuantity > 0, entry.CreatedAt));
        }

        return new PagedResult<WishlistItemResponse>(items, result.Page, result.PageSize, result.TotalCount);
    }

    public async Task<WishlistItemResponse> AddAsync(string tenantId, string businessId, string customerUserId, string productId, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        var existing = await wishlist.FindOneAsync(
            w => w.BusinessId == businessId && w.CustomerUserId == customerUserId && w.ProductId == productId, ct);

        // Idempotent: saving something already saved is a no-op, not a 409. The user's intent
        // ("I want this on my list") is already satisfied.
        var entry = existing ?? await wishlist.AddAsync(new WishlistItem
        {
            TenantId = tenantId,
            BusinessId = businessId,
            CustomerUserId = customerUserId,
            ProductId = productId
        }, ct);

        return new WishlistItemResponse(
            entry.Id, product.Id, product.Name, product.Slug, product.Price, product.EffectivePrice,
            product.Images.FirstOrDefault(), product.StockQuantity > 0, entry.CreatedAt);
    }

    public async Task RemoveAsync(string businessId, string customerUserId, string productId, CancellationToken ct = default)
    {
        var entry = await wishlist.FindOneAsync(
            w => w.BusinessId == businessId && w.CustomerUserId == customerUserId && w.ProductId == productId, ct);

        if (entry is not null)
        {
            await wishlist.HardDeleteAsync(entry.Id, ct);
        }
    }

    public async Task<List<RecommendedProductResponse>> GetAlsoBoughtAsync(string businessId, string productId, int limit = 8, CancellationToken ct = default)
    {
        // Orders containing this product, then everything else those orders contained, ranked by
        // co-occurrence. Capped at a recent window of orders so this stays a bounded read.
        var recent = await orders.FindPagedAsync(
            o => o.BusinessId == businessId && o.Status != OrderStatus.Cancelled,
            PageRequest.Of(1, PageRequest.MaxPageSize), o => o.PlacedAt, ct: ct);

        var coOccurrence = recent.Items
            .Where(o => o.Items.Any(i => i.ProductId == productId))
            .SelectMany(o => o.Items)
            .Where(i => i.ProductId != productId)
            .GroupBy(i => i.ProductId)
            .OrderByDescending(g => g.Count())
            .Take(limit)
            .ToList();

        var results = new List<RecommendedProductResponse>();
        foreach (var group in coOccurrence)
        {
            var product = await products.GetByIdAsync(group.Key, ct);
            if (product is null || !product.IsPubliclyVisibleNow(DateTime.UtcNow))
            {
                continue;
            }

            results.Add(new RecommendedProductResponse(
                product.Id, product.Name, product.Slug, product.EffectivePrice,
                product.Images.FirstOrDefault(), group.Count()));
        }

        return results;
    }

    public async Task<List<RecommendedProductResponse>> GetRelatedAsync(string businessId, string productId, int limit = 8, CancellationToken ct = default)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        var now = DateTime.UtcNow;
        var siblings = await products.FindAsync(
            p => p.BusinessId == businessId
                 && p.CategoryId == product.CategoryId
                 && p.Id != productId
                 && p.Status == ProductStatus.Active, ct);

        return siblings
            .Where(p => p.IsPubliclyVisibleNow(now))
            .OrderByDescending(p => p.IsFeatured)
            .ThenByDescending(p => p.AverageRating)
            .Take(limit)
            .Select(p => new RecommendedProductResponse(
                p.Id, p.Name, p.Slug, p.EffectivePrice, p.Images.FirstOrDefault(), 0))
            .ToList();
    }
}

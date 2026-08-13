using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

public class ProductService(IMongoRepository<Product> products) : IProductService
{
    public async Task<ProductResponse> CreateAsync(string tenantId, string businessId, CreateProductRequest request, CancellationToken ct = default)
    {
        var slug = await GenerateUniqueSlugAsync(businessId, request.Slug ?? request.Name, ct);

        var product = new Product
        {
            TenantId = tenantId,
            BusinessId = businessId,
            CategoryId = request.CategoryId,
            Name = request.Name,
            Slug = slug,
            Sku = request.Sku,
            Description = request.Description,
            Price = request.Price,
            CompareAtPrice = request.CompareAtPrice,
            StockQuantity = request.StockQuantity,
            TrackInventory = request.TrackInventory,
            Images = request.Images ?? [],
            Tags = request.Tags ?? [],
            Status = ProductStatus.Draft
        };

        await products.AddAsync(product, ct);
        return Map(product);
    }

    public async Task<List<ProductResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default)
    {
        var list = await products.FindAsync(p => p.BusinessId == businessId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<List<ProductResponse>> GetPublicCatalogAsync(string businessId, string? categoryId, string? search, CancellationToken ct = default)
    {
        var list = await products.FindAsync(p => p.BusinessId == businessId && p.Status == ProductStatus.Active, ct);

        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            list = list.Where(p => p.CategoryId == categoryId).ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            list = list.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                                    || p.Description.Contains(search, StringComparison.OrdinalIgnoreCase))
                       .ToList();
        }

        return list.Select(Map).ToList();
    }

    public async Task<ProductResponse> GetByIdAsync(string businessId, string productId, CancellationToken ct = default)
    {
        var product = await GetScopedAsync(businessId, productId, ct);
        return Map(product);
    }

    public async Task<ProductResponse> UpdateAsync(string tenantId, string businessId, string productId, UpdateProductRequest request, CancellationToken ct = default)
    {
        var product = await GetScopedAsync(businessId, productId, ct);
        if (product.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        product.CategoryId = request.CategoryId;
        product.Name = request.Name;
        product.Description = request.Description;
        product.Price = request.Price;
        product.CompareAtPrice = request.CompareAtPrice;
        product.DiscountPercent = request.DiscountPercent;
        product.DiscountExpiresAt = request.DiscountExpiresAt;
        product.StockQuantity = request.StockQuantity;
        product.TrackInventory = request.TrackInventory;
        product.Images = request.Images;
        product.Tags = request.Tags;
        product.UpdatedAt = DateTime.UtcNow;

        await products.UpdateAsync(product, ct);
        return Map(product);
    }

    public async Task<ProductResponse> UpdateStatusAsync(string tenantId, string businessId, string productId, ProductStatus status, CancellationToken ct = default)
    {
        var product = await GetScopedAsync(businessId, productId, ct);
        if (product.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        product.Status = status;
        product.UpdatedAt = DateTime.UtcNow;
        await products.UpdateAsync(product, ct);
        return Map(product);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string productId, CancellationToken ct = default)
    {
        var product = await GetScopedAsync(businessId, productId, ct);
        if (product.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        await products.DeleteAsync(productId, ct);
    }

    private async Task<Product> GetScopedAsync(string businessId, string productId, CancellationToken ct)
    {
        var product = await products.GetByIdAsync(productId, ct);
        if (product is null || product.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        return product;
    }

    private async Task<string> GenerateUniqueSlugAsync(string businessId, string seed, CancellationToken ct)
    {
        var baseSlug = SlugHelper.Slugify(seed);
        var attempt = 0;
        while (true)
        {
            var candidate = SlugHelper.WithSuffix(baseSlug, attempt);
            var taken = await products.ExistsAsync(p => p.BusinessId == businessId && p.Slug == candidate, ct);
            if (!taken)
            {
                return candidate;
            }

            attempt++;
        }
    }

    private static ProductResponse Map(Product p) => new(
        p.Id, p.BusinessId, p.CategoryId, p.Name, p.Slug, p.Sku, p.Description, p.Price, p.CompareAtPrice,
        p.DiscountPercent, p.DiscountExpiresAt, p.EffectivePrice, p.StockQuantity, p.TrackInventory,
        p.Images, p.Tags, p.Status);
}

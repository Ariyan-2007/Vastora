using System.Text.RegularExpressions;
using MongoDB.Bson;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

public class ProductService(IMongoRepository<Product> products, IMongoRepository<TenantAccount> tenants) : IProductService
{
    public async Task<ProductResponse> CreateAsync(string tenantId, string businessId, CreateProductRequest request, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(TenantAccount), tenantId);
        var limits = SubscriptionPlanLimits.For(tenant.Plan);
        if (limits.MaxProductsPerBusiness is int maxProducts)
        {
            var productCount = await products.CountAsync(p => p.BusinessId == businessId, ct);
            if (productCount >= maxProducts)
            {
                throw new ConflictException(
                    $"Your '{tenant.Plan}' plan allows up to {maxProducts} product(s) per Business. Upgrade your plan to add more.");
            }
        }

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
            Status = ProductStatus.Draft,
            Variants = MapVariants(request.Variants)
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
        var hasCategory = !string.IsNullOrWhiteSpace(categoryId);
        var hasSearch = !string.IsNullOrWhiteSpace(search);

        // Filtering is pushed into the server-side query (not fetch-then-LINQ-filter) — §9.5.
        // Regex.IsMatch(field, pattern, RegexOptions.IgnoreCase) is the MongoDB LINQ provider's
        // documented translation for case-insensitive substring matching; Regex.Escape guards
        // against the search string being interpreted as a regex pattern instead of a literal.
        List<Product> list;
        if (hasCategory && hasSearch)
        {
            var pattern = Regex.Escape(search!);
            list = await products.FindAsync(p =>
                p.BusinessId == businessId && p.Status == ProductStatus.Active && p.CategoryId == categoryId
                && (Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase) || Regex.IsMatch(p.Description, pattern, RegexOptions.IgnoreCase)),
                ct);
        }
        else if (hasCategory)
        {
            list = await products.FindAsync(p =>
                p.BusinessId == businessId && p.Status == ProductStatus.Active && p.CategoryId == categoryId, ct);
        }
        else if (hasSearch)
        {
            var pattern = Regex.Escape(search!);
            list = await products.FindAsync(p =>
                p.BusinessId == businessId && p.Status == ProductStatus.Active
                && (Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase) || Regex.IsMatch(p.Description, pattern, RegexOptions.IgnoreCase)),
                ct);
        }
        else
        {
            list = await products.FindAsync(p => p.BusinessId == businessId && p.Status == ProductStatus.Active, ct);
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
        product.TrackInventory = request.TrackInventory;
        product.ReorderThreshold = request.ReorderThreshold;
        product.ReorderQuantity = request.ReorderQuantity;
        product.Images = request.Images;
        product.Tags = request.Tags;
        product.Variants = MapVariants(request.Variants);
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

    public async Task<ProductResponse> AddImageAsync(string tenantId, string businessId, string productId, string imageUrl, CancellationToken ct = default)
    {
        var product = await GetScopedAsync(businessId, productId, ct);
        if (product.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(Product), productId);
        }

        product.Images.Add(imageUrl);
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

    private static List<ProductVariant> MapVariants(List<ProductVariantRequest>? requests) =>
        requests?.Select(v => new ProductVariant
        {
            Id = string.IsNullOrWhiteSpace(v.Id) ? ObjectId.GenerateNewId().ToString() : v.Id,
            AttributeSummary = v.AttributeSummary,
            Sku = v.Sku,
            PriceOverride = v.PriceOverride,
            StockQuantity = v.StockQuantity
        }).ToList() ?? [];

    private static ProductResponse Map(Product p) => new(
        p.Id, p.BusinessId, p.CategoryId, p.Name, p.Slug, p.Sku, p.Description, p.Price, p.CompareAtPrice,
        p.DiscountPercent, p.DiscountExpiresAt, p.EffectivePrice, p.StockQuantity, p.TrackInventory,
        p.ReorderThreshold, p.ReorderQuantity, p.Images, p.Tags, p.Status,
        p.Variants.Select(v => new ProductVariantResponse(v.Id, v.AttributeSummary, v.Sku, v.PriceOverride, v.StockQuantity)).ToList());
}

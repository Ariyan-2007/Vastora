using System.Text.RegularExpressions;
using MongoDB.Bson;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

public class ProductService(IMongoRepository<Product> products, IMongoRepository<TenantAccount> tenants, IMongoRepository<Category> categories) : IProductService
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
            Variants = MapVariants(request.Variants),
            CostPrice = request.CostPrice,
            Brand = request.Brand,
            Barcode = request.Barcode,
            WeightKg = request.WeightKg,
            LengthCm = request.LengthCm,
            WidthCm = request.WidthCm,
            HeightCm = request.HeightCm,
            MetaTitle = request.MetaTitle,
            MetaDescription = request.MetaDescription,
            PublishedAt = request.PublishedAt,
            UnpublishedAt = request.UnpublishedAt,
            IsFeatured = request.IsFeatured,
            SortWeight = request.SortWeight,
            TaxClass = request.TaxClass
        };

        await products.AddAsync(product, ct);
        return Map(product);
    }

    public async Task<PagedResult<ProductResponse>> GetForBusinessAsync(string businessId, PageRequest page, string? search, CancellationToken ct = default)
    {
        var pattern = string.IsNullOrWhiteSpace(search) ? null : Regex.Escape(search.Trim());

        var result = await products.FindPagedAsync(
            p => p.BusinessId == businessId
                 && (pattern == null
                     || Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase)
                     || Regex.IsMatch(p.Sku, pattern, RegexOptions.IgnoreCase)),
            page, p => p.CreatedAt, ct: ct);

        return result.Map(Map);
    }

    public async Task<PagedResult<ProductResponse>> GetPublicCatalogAsync(string businessId, CatalogQuery query, CancellationToken ct = default)
    {
        var matched = await QueryCatalogAsync(businessId, query, ct);

        // Sorting and paging happen in memory *after* the database filter, deliberately. The
        // filter is what bounds the read; the sort keys the storefront needs (Relevance blends
        // IsFeatured/SortWeight/recency, BestSelling would need an order join) can't all be
        // expressed as a single Mongo sort, and re-sorting a business's already-filtered catalog
        // is cheap. If a tenant's catalog ever outgrows this, Atlas Search is the answer — see
        // §9.29, which says as much.
        var sorted = ApplySort(matched, query.Sort);
        var page = PageRequest.Of(query.Page, query.PageSize);

        var items = sorted.Skip(page.Skip).Take(page.PageSize).Select(MapPublic).ToList();
        return new PagedResult<ProductResponse>(items, page.Page, page.PageSize, matched.Count);
    }

    public async Task<CatalogFacetsResponse> GetCatalogFacetsAsync(string businessId, CatalogQuery query, CancellationToken ct = default)
    {
        var matched = await QueryCatalogAsync(businessId, query, ct);

        return new CatalogFacetsResponse(
            [.. matched.GroupBy(p => p.CategoryId)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .Select(g => new FacetValue(g.Key, g.Count()))
                .OrderByDescending(f => f.Count)],
            [.. matched.GroupBy(p => p.Brand)
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => new FacetValue(g.Key, g.Count()))
                .OrderByDescending(f => f.Count)],
            [.. matched.SelectMany(p => p.Tags)
                .GroupBy(t => t)
                .Select(g => new FacetValue(g.Key, g.Count()))
                .OrderByDescending(f => f.Count)
                .Take(30)],
            matched.Count == 0 ? 0m : matched.Min(p => p.EffectivePrice),
            matched.Count == 0 ? 0m : matched.Max(p => p.EffectivePrice),
            matched.Count(p => !p.TrackInventory || p.StockQuantity > 0),
            matched.Count);
    }

    /// <summary>
    /// The shared filter behind both the catalog page and its facet counts, so a facet can never
    /// promise a count the listing then fails to deliver.
    ///
    /// Everything expressible as a Mongo predicate is (§9.5's rule, extended); the publish-window
    /// check is applied afterwards because "null OR &lt;= now" across two nullable dates reads far
    /// worse in a predicate than <c>IsPubliclyVisibleNow</c> does in C#.
    /// </summary>
    private async Task<List<Product>> QueryCatalogAsync(string businessId, CatalogQuery query, CancellationToken ct)
    {
        var pattern = string.IsNullOrWhiteSpace(query.Search) ? null : Regex.Escape(query.Search.Trim());
        var categoryIds = await ResolveCategoryAndDescendantIdsAsync(
            businessId, string.IsNullOrWhiteSpace(query.CategoryId) ? null : query.CategoryId, ct);
        var brand = string.IsNullOrWhiteSpace(query.Brand) ? null : query.Brand;

        var candidates = await products.FindAsync(
            p => p.BusinessId == businessId
                 && p.Status == ProductStatus.Active
                 && (categoryIds == null || categoryIds.Contains(p.CategoryId))
                 && (brand == null || p.Brand == brand)
                 && (query.MinPrice == null || p.Price >= query.MinPrice)
                 && (query.MaxPrice == null || p.Price <= query.MaxPrice)
                 && (query.MinRating == null || p.AverageRating >= query.MinRating)
                 && (query.FeaturedOnly != true || p.IsFeatured)
                 && (pattern == null
                     || Regex.IsMatch(p.Name, pattern, RegexOptions.IgnoreCase)
                     || Regex.IsMatch(p.Description, pattern, RegexOptions.IgnoreCase)
                     || Regex.IsMatch(p.Brand, pattern, RegexOptions.IgnoreCase)),
            ct);

        var now = DateTime.UtcNow;
        var filtered = candidates.Where(p => p.IsPubliclyVisibleNow(now));

        if (query.InStockOnly == true)
        {
            filtered = filtered.Where(p => !p.TrackInventory || p.StockQuantity > 0);
        }

        if (query.Tags is { Count: > 0 })
        {
            filtered = filtered.Where(p => query.Tags.All(t => p.Tags.Contains(t)));
        }

        return [.. filtered];
    }

    /// <summary>
    /// A parent category's page must show its subcategories' products too — browsing "Electronics"
    /// with nothing under it isn't a category page, it's an empty page (§9.5/§9.41's follow-up).
    /// Returns null when no categoryId was requested (no filter at all), or the requested id plus
    /// every descendant found by walking Category.ParentCategoryId — the same flat-collection tree
    /// walk CategoryService.GetTreeAsync uses, just flattened to an id set instead of a tree.
    /// </summary>
    private async Task<HashSet<string>?> ResolveCategoryAndDescendantIdsAsync(string businessId, string? categoryId, CancellationToken ct)
    {
        if (categoryId is null)
        {
            return null;
        }

        var all = await categories.FindAsync(c => c.BusinessId == businessId, ct);
        var byParent = all.ToLookup(c => c.ParentCategoryId);

        var ids = new HashSet<string> { categoryId };
        var frontier = new Queue<string>([categoryId]);
        while (frontier.Count > 0)
        {
            foreach (var child in byParent[frontier.Dequeue()])
            {
                if (ids.Add(child.Id))
                {
                    frontier.Enqueue(child.Id);
                }
            }
        }

        return ids;
    }

    private static List<Product> ApplySort(List<Product> items, ProductSort sort) => sort switch
    {
        ProductSort.Newest => [.. items.OrderByDescending(p => p.CreatedAt)],
        ProductSort.PriceAscending => [.. items.OrderBy(p => p.EffectivePrice)],
        ProductSort.PriceDescending => [.. items.OrderByDescending(p => p.EffectivePrice)],
        ProductSort.TopRated => [.. items.OrderByDescending(p => p.AverageRating).ThenByDescending(p => p.ReviewCount)],
        ProductSort.NameAscending => [.. items.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)],

        // BestSelling has no sales counter on Product to sort by and computing one per request
        // would mean scanning orders — ReviewCount is the honest available proxy for "people
        // actually bought this", and is documented as such rather than silently substituted.
        ProductSort.BestSelling => [.. items.OrderByDescending(p => p.ReviewCount).ThenByDescending(p => p.AverageRating)],

        // Relevance is the merchandising default: what the seller promoted, then what they
        // ordered, then what is new.
        _ => [.. items.OrderByDescending(p => p.IsFeatured).ThenBy(p => p.SortWeight).ThenByDescending(p => p.CreatedAt)]
    };

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
        product.CostPrice = request.CostPrice;
        product.Brand = request.Brand;
        product.Barcode = request.Barcode;
        product.WeightKg = request.WeightKg;
        product.LengthCm = request.LengthCm;
        product.WidthCm = request.WidthCm;
        product.HeightCm = request.HeightCm;
        product.MetaTitle = request.MetaTitle;
        product.MetaDescription = request.MetaDescription;
        product.PublishedAt = request.PublishedAt;
        product.UnpublishedAt = request.UnpublishedAt;
        product.IsFeatured = request.IsFeatured;
        product.SortWeight = request.SortWeight;
        product.TaxClass = request.TaxClass;

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

        // Same slug-retirement as CategoryService — the unique index outlives the soft delete.
        product.Slug = $"{product.Slug}-deleted-{DateTime.UtcNow:yyyyMMddHHmmss}";
        await products.UpdateAsync(product, ct);
        await products.DeleteAsync(productId, ct: ct);
    }

    public async Task<ProductImportResult> ImportAsync(
        string tenantId, string businessId, IReadOnlyList<ProductImportRow> rows,
        IReadOnlyDictionary<string, string> categoryIdsByName, CancellationToken ct = default)
    {
        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<string>();

        // Fetched once rather than per row: an import of 2,000 SKUs would otherwise be 2,000
        // extra reads just to decide insert-vs-update.
        var existingBySku = (await products.FindAsync(p => p.BusinessId == businessId, ct))
            .Where(p => !string.IsNullOrWhiteSpace(p.Sku))
            .GroupBy(p => p.Sku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rowNumber = 1;
        foreach (var row in rows)
        {
            rowNumber++;

            if (string.IsNullOrWhiteSpace(row.Sku) || string.IsNullOrWhiteSpace(row.Name))
            {
                errors.Add($"Row {rowNumber}: sku and name are both required.");
                skipped++;
                continue;
            }

            if (!categoryIdsByName.TryGetValue(row.CategoryName ?? string.Empty, out var categoryId))
            {
                errors.Add($"Row {rowNumber} ('{row.Sku}'): no category named '{row.CategoryName}'.");
                skipped++;
                continue;
            }

            var tags = string.IsNullOrWhiteSpace(row.Tags)
                ? []
                : row.Tags.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            if (existingBySku.TryGetValue(row.Sku, out var existing))
            {
                // SKU is the natural key, so a re-import is an update. Status is deliberately left
                // alone: a spreadsheet round-trip must not silently republish something the
                // merchant took down.
                existing.Name = row.Name;
                existing.CategoryId = categoryId;
                existing.Description = row.Description ?? string.Empty;
                existing.Price = row.Price;
                existing.CostPrice = row.CostPrice;
                existing.Brand = row.Brand ?? string.Empty;
                existing.Barcode = row.Barcode ?? string.Empty;
                existing.WeightKg = row.WeightKg;
                existing.Tags = tags;

                await products.UpdateAsync(existing, ct);
                updated++;
                continue;
            }

            // Stock is set directly on insert rather than through IInventoryService: there is no
            // prior balance to audit a delta against, and the product does not exist yet. Every
            // subsequent change still goes through the movement ledger (§9.15a).
            var product = new Product
            {
                TenantId = tenantId,
                BusinessId = businessId,
                CategoryId = categoryId,
                Name = row.Name,
                Slug = await GenerateUniqueSlugAsync(businessId, row.Name, ct),
                Sku = row.Sku,
                Description = row.Description ?? string.Empty,
                Price = row.Price,
                CostPrice = row.CostPrice,
                StockQuantity = row.StockQuantity,
                Brand = row.Brand ?? string.Empty,
                Barcode = row.Barcode ?? string.Empty,
                WeightKg = row.WeightKg,
                Tags = tags,
                Status = ProductStatus.Draft
            };

            await products.AddAsync(product, ct);
            existingBySku[row.Sku] = product;
            created++;
        }

        return new ProductImportResult(created, updated, skipped, errors);
    }

    public async Task<List<ProductImportRow>> ExportAsync(
        string businessId, IReadOnlyDictionary<string, string> categoryNamesById, CancellationToken ct = default)
    {
        var all = await products.FindAsync(p => p.BusinessId == businessId, ct);

        return [.. all.Select(p => new ProductImportRow(
            p.Sku, p.Name,
            categoryNamesById.TryGetValue(p.CategoryId, out var name) ? name : string.Empty,
            p.Description, p.Price, p.CostPrice, p.StockQuantity, p.Brand, p.Barcode, p.WeightKg,
            string.Join('|', p.Tags)))];
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

    private static ProductResponse Map(Product p) => Build(p, includeCost: true);

    /// <summary>
    /// The public storefront projection. Identical to <see cref="Map"/> except that CostPrice and
    /// UnitMargin are nulled — what a business pays its suppliers is not something its customers
    /// (or its competitors browsing the shop) get to read.
    /// </summary>
    private static ProductResponse MapPublic(Product p) => Build(p, includeCost: false);

    private static ProductResponse Build(Product p, bool includeCost) => new(
        p.Id, p.BusinessId, p.CategoryId, p.Name, p.Slug, p.Sku, p.Description, p.Price,
        includeCost ? p.CostPrice : null,
        includeCost ? p.UnitMargin : null,
        p.CompareAtPrice,
        p.DiscountPercent, p.DiscountExpiresAt, p.EffectivePrice, p.StockQuantity, p.TrackInventory,
        p.ReorderThreshold, p.ReorderQuantity, p.Images, p.Tags, p.Status,
        [.. p.Variants.Select(v => new ProductVariantResponse(v.Id, v.AttributeSummary, v.Sku, v.PriceOverride, v.StockQuantity))],
        p.AverageRating, p.ReviewCount, p.Brand, p.Barcode, p.WeightKg,
        p.MetaTitle, p.MetaDescription, p.PublishedAt, p.UnpublishedAt, p.IsFeatured, p.SortWeight, p.TaxClass,
        !p.TrackInventory || p.StockQuantity > 0 || p.Variants.Any(v => v.StockQuantity > 0));
}

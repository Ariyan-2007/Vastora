using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

/// <summary>Catalog-only — see ProductVariant's domain XML doc for the Cart/Order boundary.</summary>
public record ProductVariantResponse(string Id, string AttributeSummary, string Sku, decimal? PriceOverride, int StockQuantity);

public record ProductVariantRequest(string? Id, string AttributeSummary, string Sku, decimal? PriceOverride, int StockQuantity);

/// <summary>
/// <paramref name="CostPrice"/> and <paramref name="UnitMargin"/> are commercially sensitive —
/// the public catalog mapper strips them, and only BackOffice reads see them populated.
/// </summary>
public record ProductResponse(
    string Id,
    string BusinessId,
    string CategoryId,
    string Name,
    string Slug,
    string Sku,
    string Description,
    decimal Price,
    decimal? CostPrice,
    decimal? UnitMargin,
    decimal? CompareAtPrice,
    decimal? DiscountPercent,
    DateTime? DiscountExpiresAt,
    decimal EffectivePrice,
    int StockQuantity,
    bool TrackInventory,
    int? ReorderThreshold,
    int? ReorderQuantity,
    List<string> Images,
    List<string> Tags,
    ProductStatus Status,
    List<ProductVariantResponse> Variants,
    double AverageRating,
    int ReviewCount,
    string Brand,
    string Barcode,
    decimal? WeightKg,
    string MetaTitle,
    string MetaDescription,
    DateTime? PublishedAt,
    DateTime? UnpublishedAt,
    bool IsFeatured,
    int SortWeight,
    string TaxClass,
    bool IsAvailable);

public record CreateProductRequest(
    string CategoryId,
    string Name,
    string? Slug,
    string Sku,
    string Description,
    decimal Price,
    decimal? CompareAtPrice,
    int StockQuantity,
    bool TrackInventory,
    List<string>? Images,
    List<string>? Tags,
    List<ProductVariantRequest>? Variants,
    /// <summary>§9.31. Without this, COGS and gross margin cannot be computed at all.</summary>
    decimal? CostPrice = null,
    string Brand = "",
    string Barcode = "",
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    string MetaTitle = "",
    string MetaDescription = "",
    DateTime? PublishedAt = null,
    DateTime? UnpublishedAt = null,
    bool IsFeatured = false,
    int SortWeight = 0,
    string TaxClass = "");

/// <summary>
/// No StockQuantity here (§9.15c) — stock only changes through checkout, order cancellation, or
/// POST .../products/{id}/stock-adjustments, all of which log a StockMovement. A general profile
/// edit can no longer silently overwrite it.
/// </summary>
public record UpdateProductRequest(
    string CategoryId,
    string Name,
    string Description,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? DiscountPercent,
    DateTime? DiscountExpiresAt,
    bool TrackInventory,
    int? ReorderThreshold,
    int? ReorderQuantity,
    List<string> Images,
    List<string> Tags,
    List<ProductVariantRequest>? Variants,
    decimal? CostPrice = null,
    string Brand = "",
    string Barcode = "",
    decimal? WeightKg = null,
    decimal? LengthCm = null,
    decimal? WidthCm = null,
    decimal? HeightCm = null,
    string MetaTitle = "",
    string MetaDescription = "",
    DateTime? PublishedAt = null,
    DateTime? UnpublishedAt = null,
    bool IsFeatured = false,
    int SortWeight = 0,
    string TaxClass = "");

public record UpdateProductStatusRequest(ProductStatus Status);

/// <summary>
/// §9.29. Everything the public catalog can be sliced by. Applied in the database query, not in
/// memory — the pre-§9B catalog read pulled every Active product for the business and filtered
/// with LINQ.
/// </summary>
public record CatalogQuery(
    string? CategoryId = null,
    string? Search = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    string? Brand = null,
    List<string>? Tags = null,
    bool? InStockOnly = null,
    double? MinRating = null,
    bool? FeaturedOnly = null,
    ProductSort Sort = ProductSort.Relevance,
    int Page = 1,
    int PageSize = 24);

/// <summary>One selectable filter value with the count of products behind it — §9.29.</summary>
public record FacetValue(string Value, int Count);

public record CatalogFacetsResponse(
    List<FacetValue> Categories,
    List<FacetValue> Brands,
    List<FacetValue> Tags,
    decimal MinPrice,
    decimal MaxPrice,
    int InStockCount,
    int TotalCount);

/// <summary>§9.28. One CSV row, already parsed. Slug and variants are deliberately out of scope for bulk import.</summary>
public record ProductImportRow(
    string Sku,
    string Name,
    string CategoryName,
    string Description,
    decimal Price,
    decimal? CostPrice,
    int StockQuantity,
    string Brand,
    string Barcode,
    decimal? WeightKg,
    string Tags);

public record ProductImportResult(int Created, int Updated, int Skipped, List<string> Errors);

using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

/// <summary>Catalog-only — see ProductVariant's domain XML doc for the Cart/Order boundary.</summary>
public record ProductVariantResponse(string Id, string AttributeSummary, string Sku, decimal? PriceOverride, int StockQuantity);

public record ProductVariantRequest(string? Id, string AttributeSummary, string Sku, decimal? PriceOverride, int StockQuantity);

public record ProductResponse(
    string Id,
    string BusinessId,
    string CategoryId,
    string Name,
    string Slug,
    string Sku,
    string Description,
    decimal Price,
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
    List<ProductVariantResponse> Variants);

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
    List<ProductVariantRequest>? Variants);

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
    List<ProductVariantRequest>? Variants);

public record UpdateProductStatusRequest(ProductStatus Status);

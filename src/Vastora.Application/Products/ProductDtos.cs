using Vastora.Domain.Enums;

namespace Vastora.Application.Products;

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
    List<string> Images,
    List<string> Tags,
    ProductStatus Status);

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
    List<string>? Tags);

public record UpdateProductRequest(
    string CategoryId,
    string Name,
    string Description,
    decimal Price,
    decimal? CompareAtPrice,
    decimal? DiscountPercent,
    DateTime? DiscountExpiresAt,
    int StockQuantity,
    bool TrackInventory,
    List<string> Images,
    List<string> Tags);

public record UpdateProductStatusRequest(ProductStatus Status);

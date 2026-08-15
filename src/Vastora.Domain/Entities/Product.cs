using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// One purchasable variation of a Product (e.g. "Red / Large") — §9.5. Embedded, Mongo-idiomatic,
/// same reasoning as OrderItem/CartItem (see §8 of the blueprint). Catalog-only for now: Cart and
/// Order still reference a bare ProductId with no variant selection — see the blueprint for the
/// exact boundary of what this covers.
/// </summary>
public class ProductVariant
{
    public string Id { get; set; } = string.Empty;

    public string AttributeSummary { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public decimal? PriceOverride { get; set; }

    public int StockQuantity { get; set; }
}

public class Product : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string Sku { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public decimal? CompareAtPrice { get; set; }

    public decimal? DiscountPercent { get; set; }

    public DateTime? DiscountExpiresAt { get; set; }

    public int StockQuantity { get; set; }

    public bool TrackInventory { get; set; } = true;

    /// <summary>Null means no reorder alert is configured for this product — §9.15b.</summary>
    public int? ReorderThreshold { get; set; }

    /// <summary>Informational only — how much to reorder when the threshold is hit. Not enforced anywhere.</summary>
    public int? ReorderQuantity { get; set; }

    public List<string> Images { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    public List<ProductVariant> Variants { get; set; } = [];

    public decimal EffectivePrice =>
        DiscountPercent is > 0 && (DiscountExpiresAt is null || DiscountExpiresAt > DateTime.UtcNow)
            ? Math.Round(Price * (1 - DiscountPercent.Value / 100m), 2)
            : Price;
}

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

    /// <summary>
    /// What this unit cost the business to acquire — §9.31. Drives COGS, gross margin, and
    /// inventory-valued-at-cost. Null means "not recorded", and every consumer treats a null
    /// cost as zero margin contribution rather than guessing, so a business that never fills
    /// this in gets honest gaps in its P&amp;L instead of invented numbers.
    /// </summary>
    public decimal? CostPrice { get; set; }

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

    /// <summary>Denormalised from the Review collection (§9.25) so the catalog can sort by rating without a join.</summary>
    public double AverageRating { get; set; }

    public int ReviewCount { get; set; }

    // --- §9.28: fields downstream features need before they can exist at all ---

    /// <summary>Kilograms. Null = unknown; weight-based shipping rates (§9.20) skip such products.</summary>
    public decimal? WeightKg { get; set; }

    public decimal? LengthCm { get; set; }

    public decimal? WidthCm { get; set; }

    public decimal? HeightCm { get; set; }

    public string Brand { get; set; } = string.Empty;

    /// <summary>GTIN/UPC/EAN — needed for marketplace feeds and warehouse scanning.</summary>
    public string Barcode { get; set; } = string.Empty;

    public string MetaTitle { get; set; } = string.Empty;

    public string MetaDescription { get; set; } = string.Empty;

    /// <summary>Scheduled go-live. An Active product before this instant is still withheld from the public catalog.</summary>
    public DateTime? PublishedAt { get; set; }

    /// <summary>Scheduled retirement, same idea in reverse.</summary>
    public DateTime? UnpublishedAt { get; set; }

    public bool IsFeatured { get; set; }

    /// <summary>Lower sorts first within a category. Ties fall back to newest-first.</summary>
    public int SortWeight { get; set; }

    /// <summary>Tax class key resolved against the Business's tax rates (§9.19). Empty = the Business default rate.</summary>
    public string TaxClass { get; set; } = string.Empty;

    /// <summary>True once PublishedAt/UnpublishedAt allow it — Status alone is not enough (§9.28).</summary>
    public bool IsPubliclyVisibleNow(DateTime now) =>
        Status == ProductStatus.Active
        && (PublishedAt is null || PublishedAt <= now)
        && (UnpublishedAt is null || UnpublishedAt > now);

    /// <summary>Margin per unit, null when CostPrice was never recorded — never guessed as zero cost.</summary>
    public decimal? UnitMargin => CostPrice is null ? null : EffectivePrice - CostPrice.Value;

    public decimal EffectivePrice =>
        DiscountPercent is > 0 && (DiscountExpiresAt is null || DiscountExpiresAt > DateTime.UtcNow)
            ? Math.Round(Price * (1 - DiscountPercent.Value / 100m), 2)
            : Price;
}

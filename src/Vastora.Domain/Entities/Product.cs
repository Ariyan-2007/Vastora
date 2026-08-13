using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

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

    public List<string> Images { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    public ProductStatus Status { get; set; } = ProductStatus.Draft;

    public decimal EffectivePrice =>
        DiscountPercent is > 0 && (DiscountExpiresAt is null || DiscountExpiresAt > DateTime.UtcNow)
            ? Math.Round(Price * (1 - DiscountPercent.Value / 100m), 2)
            : Price;
}

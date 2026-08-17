using Vastora.Domain.Common;

namespace Vastora.Domain.Entities;

/// <summary>One priced band within a zone. The first band whose bounds contain the order wins.</summary>
public class ShippingRate
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Shown to the customer at checkout — "Standard (3–5 days)", "Express".</summary>
    public string Name { get; set; } = string.Empty;

    public decimal Price { get; set; }

    /// <summary>Inclusive lower bound on cart subtotal. Null = no lower bound.</summary>
    public decimal? MinOrderSubtotal { get; set; }

    /// <summary>Exclusive upper bound — this is how a free-shipping-over-X threshold is expressed.</summary>
    public decimal? MaxOrderSubtotal { get; set; }

    /// <summary>Inclusive lower bound on total cart weight in kg. Null = ignore weight.</summary>
    public decimal? MinWeightKg { get; set; }

    public decimal? MaxWeightKg { get; set; }

    public int? EstimatedDaysMin { get; set; }

    public int? EstimatedDaysMax { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>
/// §9.20. A destination band with its own rate table, replacing the single flat
/// Business.DefaultDeliveryFee. Matching is country-then-region, most specific first; a zone
/// with an empty Countries list is the catch-all fallback.
/// </summary>
public class ShippingZone : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>ISO country codes, case-insensitive. Empty = matches anywhere (the fallback zone).</summary>
    public List<string> Countries { get; set; } = [];

    /// <summary>Optional narrowing within the countries — state/division/city names as stored on Address.</summary>
    public List<string> Regions { get; set; } = [];

    public List<ShippingRate> Rates { get; set; } = [];

    /// <summary>Lower wins when several zones match. A fallback zone should sit at the highest number.</summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;
}

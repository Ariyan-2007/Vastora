using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>What a promotion gives away once its conditions are met.</summary>
public enum PromotionEffect
{
    PercentageOff = 1,
    FixedAmountOff = 2,
    FreeShipping = 3,
    /// <summary>Buy X of the scoped products, get Y of them free (cheapest lines discounted).</summary>
    BuyXGetY = 4
}

/// <summary>Which lines of the cart a promotion applies to.</summary>
public enum PromotionScope
{
    /// <summary>The whole cart subtotal.</summary>
    Order = 1,
    /// <summary>Only lines whose product is in ProductIds.</summary>
    Products = 2,
    /// <summary>Only lines whose product sits in CategoryIds.</summary>
    Categories = 3
}

/// <summary>
/// §9.23. The general discount rule Coupon could never express. Coupon is deliberately left in
/// place and untouched — existing codes keep working through the same path they always did —
/// and Promotion sits alongside it for everything else: automatic (no-code) discounts, BOGO,
/// free shipping, category-scoped offers, per-customer usage caps, and group targeting.
/// </summary>
public class Promotion : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Null means automatic — it applies with no code typed, which Coupon cannot do.</summary>
    public string? Code { get; set; }

    public PromotionEffect Effect { get; set; } = PromotionEffect.PercentageOff;

    public PromotionScope Scope { get; set; } = PromotionScope.Order;

    /// <summary>Percent for PercentageOff, currency amount for FixedAmountOff, ignored otherwise.</summary>
    public decimal Value { get; set; }

    public List<string> ProductIds { get; set; } = [];

    public List<string> CategoryIds { get; set; } = [];

    /// <summary>BuyXGetY only.</summary>
    public int BuyQuantity { get; set; }

    public int GetQuantity { get; set; }

    public decimal? MinOrderAmount { get; set; }

    /// <summary>Empty = everyone. Otherwise only members of these groups qualify.</summary>
    public List<string> CustomerGroupIds { get; set; } = [];

    public bool FirstOrderOnly { get; set; }

    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    /// <summary>Per-customer cap — the gap that made Coupon.MaxUses (global only) abusable.</summary>
    public int? MaxUsesPerCustomer { get; set; }

    /// <summary>Lower runs first. Determines which promotion wins when Stackable is false.</summary>
    public int Priority { get; set; }

    /// <summary>False means this promotion suppresses every lower-priority one.</summary>
    public bool Stackable { get; set; }

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;

    public DateTime? EndsAt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsLiveNow(DateTime now) =>
        IsActive
        && StartsAt <= now
        && (EndsAt is null || EndsAt > now)
        && (MaxUses is null || UsedCount < MaxUses);
}

using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

public class Coupon : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public DiscountType DiscountType { get; set; } = DiscountType.Percentage;

    public decimal DiscountValue { get; set; }

    public decimal? MinOrderAmount { get; set; }

    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    public DateTime StartsAt { get; set; } = DateTime.UtcNow;

    /// <summary>§9.43. Null means the code never expires — an evergreen coupon is a legitimate
    /// design (a permanent referral or partner code), not an oversight, so this is no longer
    /// forced to always carry a date.</summary>
    public DateTime? ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>§9.43. Public is listed by the storefront's available-offers endpoint; Hidden only
    /// works when the exact code is typed — see <see cref="DiscountVisibility"/>.</summary>
    public DiscountVisibility Visibility { get; set; } = DiscountVisibility.Public;

    public bool IsValidNow =>
        IsActive
        && DateTime.UtcNow >= StartsAt
        && (ExpiresAt is null || DateTime.UtcNow <= ExpiresAt)
        && (MaxUses is null || UsedCount < MaxUses);
}

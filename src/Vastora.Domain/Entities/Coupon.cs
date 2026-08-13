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

    public DateTime ExpiresAt { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsValidNow =>
        IsActive
        && DateTime.UtcNow >= StartsAt
        && DateTime.UtcNow <= ExpiresAt
        && (MaxUses is null || UsedCount < MaxUses);
}

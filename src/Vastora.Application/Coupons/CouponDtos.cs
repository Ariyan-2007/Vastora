using Vastora.Domain.Enums;

namespace Vastora.Application.Coupons;

public record CouponResponse(
    string Id,
    string BusinessId,
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinOrderAmount,
    int? MaxUses,
    int UsedCount,
    DateTime StartsAt,
    /// <summary>§9.43. Null means the code never expires.</summary>
    DateTime? ExpiresAt,
    bool IsActive,
    DiscountVisibility Visibility);

public record CreateCouponRequest(
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinOrderAmount,
    int? MaxUses,
    DateTime StartsAt,
    /// <summary>§9.43. Omit or pass null for a coupon that never expires.</summary>
    DateTime? ExpiresAt,
    DiscountVisibility Visibility = DiscountVisibility.Public);

public record UpdateCouponRequest(bool IsActive, DateTime? ExpiresAt, int? MaxUses, DiscountVisibility Visibility);

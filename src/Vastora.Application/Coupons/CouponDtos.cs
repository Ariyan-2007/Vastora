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
    DateTime ExpiresAt,
    bool IsActive);

public record CreateCouponRequest(
    string Code,
    DiscountType DiscountType,
    decimal DiscountValue,
    decimal? MinOrderAmount,
    int? MaxUses,
    DateTime StartsAt,
    DateTime ExpiresAt);

public record UpdateCouponRequest(bool IsActive, DateTime ExpiresAt, int? MaxUses);

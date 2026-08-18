namespace Vastora.Application.Coupons;

public interface ICouponService
{
    Task<CouponResponse> CreateAsync(string tenantId, string businessId, CreateCouponRequest request, CancellationToken ct = default);

    Task<List<CouponResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default);

    /// <summary>§9.43. Currently-valid, non-<see cref="Vastora.Domain.Enums.DiscountVisibility.Hidden"/>
    /// coupons — what the storefront's available-offers listing is allowed to show a shopper
    /// without them having typed a code first. A Hidden coupon still works when entered; it is
    /// simply never returned here.</summary>
    Task<List<CouponResponse>> GetPublicActiveAsync(string businessId, CancellationToken ct = default);

    Task<CouponResponse> UpdateAsync(string tenantId, string businessId, string couponId, UpdateCouponRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string couponId, CancellationToken ct = default);

    /// <summary>Validates a code against an order amount and returns the discount to apply; throws if invalid.</summary>
    Task<decimal> ValidateAndPriceAsync(string businessId, string code, decimal orderAmount, CancellationToken ct = default);

    Task RegisterUsageAsync(string businessId, string code, CancellationToken ct = default);
}

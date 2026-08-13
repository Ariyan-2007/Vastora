namespace Vastora.Application.Coupons;

public interface ICouponService
{
    Task<CouponResponse> CreateAsync(string tenantId, string businessId, CreateCouponRequest request, CancellationToken ct = default);

    Task<List<CouponResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default);

    Task<CouponResponse> UpdateAsync(string tenantId, string businessId, string couponId, UpdateCouponRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string couponId, CancellationToken ct = default);

    /// <summary>Validates a code against an order amount and returns the discount to apply; throws if invalid.</summary>
    Task<decimal> ValidateAndPriceAsync(string businessId, string code, decimal orderAmount, CancellationToken ct = default);

    Task RegisterUsageAsync(string businessId, string code, CancellationToken ct = default);
}

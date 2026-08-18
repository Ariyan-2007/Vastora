using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Coupons;

public class CouponService(IMongoRepository<Coupon> coupons) : ICouponService
{
    public async Task<CouponResponse> CreateAsync(string tenantId, string businessId, CreateCouponRequest request, CancellationToken ct = default)
    {
        var code = request.Code.Trim().ToUpperInvariant();

        var taken = await coupons.ExistsAsync(c => c.BusinessId == businessId && c.Code == code, ct);
        if (taken)
        {
            throw new ConflictException($"Coupon code '{code}' already exists for this business.");
        }

        var coupon = new Coupon
        {
            TenantId = tenantId,
            BusinessId = businessId,
            Code = code,
            DiscountType = request.DiscountType,
            DiscountValue = request.DiscountValue,
            MinOrderAmount = request.MinOrderAmount,
            MaxUses = request.MaxUses,
            StartsAt = request.StartsAt,
            ExpiresAt = request.ExpiresAt,
            IsActive = true,
            Visibility = request.Visibility
        };

        await coupons.AddAsync(coupon, ct);
        return Map(coupon);
    }

    public async Task<List<CouponResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default)
    {
        var list = await coupons.FindAsync(c => c.BusinessId == businessId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<List<CouponResponse>> GetPublicActiveAsync(string businessId, CancellationToken ct = default)
    {
        var list = await coupons.FindAsync(
            c => c.BusinessId == businessId && c.IsActive && c.Visibility == DiscountVisibility.Public, ct);
        return [.. list.Where(c => c.IsValidNow).Select(Map)];
    }

    public async Task<CouponResponse> UpdateAsync(string tenantId, string businessId, string couponId, UpdateCouponRequest request, CancellationToken ct = default)
    {
        var coupon = await GetScopedAsync(tenantId, businessId, couponId, ct);
        coupon.IsActive = request.IsActive;
        coupon.ExpiresAt = request.ExpiresAt;
        coupon.MaxUses = request.MaxUses;
        coupon.Visibility = request.Visibility;
        coupon.UpdatedAt = DateTime.UtcNow;
        await coupons.UpdateAsync(coupon, ct);
        return Map(coupon);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string couponId, CancellationToken ct = default)
    {
        await GetScopedAsync(tenantId, businessId, couponId, ct);
        await coupons.DeleteAsync(couponId, ct: ct);
    }

    public async Task<decimal> ValidateAndPriceAsync(string businessId, string code, decimal orderAmount, CancellationToken ct = default)
    {
        var coupon = await coupons.FindOneAsync(c => c.BusinessId == businessId && c.Code == code.Trim().ToUpperInvariant(), ct)
            ?? throw new NotFoundException(nameof(Coupon), code);

        if (!coupon.IsValidNow)
        {
            throw new ConflictException($"Coupon '{code}' is not currently valid.");
        }

        if (coupon.MinOrderAmount is not null && orderAmount < coupon.MinOrderAmount)
        {
            throw new ConflictException($"Coupon '{code}' requires a minimum order of {coupon.MinOrderAmount}.");
        }

        return coupon.DiscountType == DiscountType.Percentage
            ? Math.Round(orderAmount * coupon.DiscountValue / 100m, 2)
            : Math.Min(coupon.DiscountValue, orderAmount);
    }

    public async Task RegisterUsageAsync(string businessId, string code, CancellationToken ct = default)
    {
        var coupon = await coupons.FindOneAsync(c => c.BusinessId == businessId && c.Code == code.Trim().ToUpperInvariant(), ct);
        if (coupon is null)
        {
            return;
        }

        coupon.UsedCount += 1;
        coupon.UpdatedAt = DateTime.UtcNow;
        await coupons.UpdateAsync(coupon, ct);
    }

    private async Task<Coupon> GetScopedAsync(string tenantId, string businessId, string couponId, CancellationToken ct)
    {
        var coupon = await coupons.GetByIdAsync(couponId, ct);
        if (coupon is null || coupon.TenantId != tenantId || coupon.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Coupon), couponId);
        }

        return coupon;
    }

    private static CouponResponse Map(Coupon c) => new(
        c.Id, c.BusinessId, c.Code, c.DiscountType, c.DiscountValue, c.MinOrderAmount,
        c.MaxUses, c.UsedCount, c.StartsAt, c.ExpiresAt, c.IsActive, c.Visibility);
}

using Vastora.Application.Coupons;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Coupons;

public class CouponServiceTests
{
    private static (CouponService Service, FakeMongoRepository<Coupon> Coupons) Create()
    {
        var coupons = new FakeMongoRepository<Coupon>();
        return (new CouponService(coupons), coupons);
    }

    [Fact]
    public async Task IsValidNow_TreatsANullExpiry_AsNeverExpiring()
    {
        var (service, coupons) = Create();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1",
            Code = "FOREVER",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            StartsAt = DateTime.UtcNow.AddYears(-1),
            ExpiresAt = null,
            IsActive = true
        });

        // §9.43: a coupon can now be created with no ExpiresAt at all — an evergreen code, not an
        // oversight. This must still price successfully years after creation.
        var discount = await service.ValidateAndPriceAsync("biz-1", "FOREVER", 100m, CancellationToken.None);

        Assert.Equal(10m, discount);
    }

    [Fact]
    public async Task GetPublicActiveAsync_ExcludesHiddenCoupons()
    {
        var (service, coupons) = Create();
        coupons.Seed(
            new Coupon
            {
                BusinessId = "biz-1",
                Code = "PUBLIC10",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 10m,
                StartsAt = DateTime.UtcNow.AddDays(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                IsActive = true,
                Visibility = DiscountVisibility.Public
            },
            new Coupon
            {
                BusinessId = "biz-1",
                Code = "VIPONLY",
                DiscountType = DiscountType.Percentage,
                DiscountValue = 25m,
                StartsAt = DateTime.UtcNow.AddDays(-1),
                ExpiresAt = DateTime.UtcNow.AddDays(30),
                IsActive = true,
                Visibility = DiscountVisibility.Hidden
            });

        // §9.43: a Hidden coupon must never surface in the storefront's available-offers listing
        // — that is the entire point of the targeted/email-only campaign case.
        var visible = await service.GetPublicActiveAsync("biz-1", CancellationToken.None);

        var code = Assert.Single(visible);
        Assert.Equal("PUBLIC10", code.Code);
    }

    [Fact]
    public async Task GetPublicActiveAsync_ExcludesExpiredCoupons()
    {
        var (service, coupons) = Create();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1",
            Code = "EXPIRED",
            DiscountType = DiscountType.Percentage,
            DiscountValue = 10m,
            StartsAt = DateTime.UtcNow.AddDays(-30),
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            IsActive = true,
            Visibility = DiscountVisibility.Public
        });

        var visible = await service.GetPublicActiveAsync("biz-1", CancellationToken.None);

        Assert.Empty(visible);
    }

    [Fact]
    public async Task ValidateAndPriceAsync_StillWorksForAHiddenCoupon_WhenTheExactCodeIsEntered()
    {
        var (service, coupons) = Create();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1",
            Code = "SECRET20",
            DiscountType = DiscountType.FixedAmount,
            DiscountValue = 20m,
            StartsAt = DateTime.UtcNow.AddDays(-1),
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            IsActive = true,
            Visibility = DiscountVisibility.Hidden
        });

        // Hidden only means "not listed" — it must still redeem normally when typed exactly,
        // which is what makes it usable as an email-only code at all.
        var discount = await service.ValidateAndPriceAsync("biz-1", "secret20", 100m, CancellationToken.None);

        Assert.Equal(20m, discount);
    }
}

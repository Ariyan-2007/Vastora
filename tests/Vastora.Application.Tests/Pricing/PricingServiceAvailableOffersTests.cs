using Vastora.Application.Coupons;
using Vastora.Application.CustomerGroups;
using Vastora.Application.GiftCards;
using Vastora.Application.Pricing;
using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Pricing;

/// <summary>§9.43. "Shown where applicable": <see cref="IPricingService.GetAvailableOffersAsync"/>
/// is what backs the storefront's available-offers listing, merging Public coupons and coded
/// Promotions and filtering out anything the current subtotal can't even qualify for on
/// MinOrderAmount alone.</summary>
public class PricingServiceAvailableOffersTests
{
    private static PricingService Create(FakeMongoRepository<Coupon> coupons, FakeMongoRepository<Promotion> promotions)
    {
        var businesses = new FakeMongoRepository<Business>();
        return new PricingService(
            new FakeMongoRepository<Product>(),
            new FakeMongoRepository<Order>(),
            new CouponService(coupons),
            new PromotionService(promotions, new FakeMongoRepository<Order>()),
            new CustomerGroupService(new FakeMongoRepository<CustomerGroup>(), new FakeMongoRepository<AppUser>()),
            new ShippingService(new FakeMongoRepository<ShippingZone>()),
            new TaxService(),
            new GiftCardService(new FakeMongoRepository<GiftCard>(), businesses),
            new StoreCreditService(new FakeMongoRepository<StoreCreditEntry>(), businesses));
    }

    [Fact]
    public async Task GetAvailableOffersAsync_MergesPublicCoupons_AndCodedPromotions()
    {
        var coupons = new FakeMongoRepository<Coupon>();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1", Code = "SAVE10", DiscountType = DiscountType.Percentage, DiscountValue = 10m,
            StartsAt = DateTime.UtcNow.AddDays(-1), ExpiresAt = DateTime.UtcNow.AddDays(10),
            IsActive = true, Visibility = DiscountVisibility.Public
        });

        var promotions = new FakeMongoRepository<Promotion>();
        promotions.Seed(new Promotion
        {
            BusinessId = "biz-1", Name = "Free Shipping Weekend", Code = "FREESHIP",
            Effect = PromotionEffect.FreeShipping, IsActive = true, StartsAt = DateTime.UtcNow.AddDays(-1),
            Visibility = DiscountVisibility.Public
        });

        var offers = await Create(coupons, promotions).GetAvailableOffersAsync("biz-1", 50m, CancellationToken.None);

        Assert.Equal(2, offers.Count);
        Assert.Contains(offers, o => o.Source == "Coupon" && o.Code == "SAVE10" && o.Summary == "10% off");
        Assert.Contains(offers, o => o.Source == "Promotion" && o.Code == "FREESHIP" && o.Summary == "Free shipping");
    }

    [Fact]
    public async Task GetAvailableOffersAsync_HidesOffers_TheSubtotalCannotYetQualifyFor()
    {
        var coupons = new FakeMongoRepository<Coupon>();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1", Code = "BIGSPENDER", DiscountType = DiscountType.FixedAmount, DiscountValue = 20m,
            MinOrderAmount = 200m, StartsAt = DateTime.UtcNow.AddDays(-1), ExpiresAt = DateTime.UtcNow.AddDays(10),
            IsActive = true, Visibility = DiscountVisibility.Public
        });

        var offers = await Create(coupons, new FakeMongoRepository<Promotion>())
            .GetAvailableOffersAsync("biz-1", 50m, CancellationToken.None);

        Assert.Empty(offers);
    }
}

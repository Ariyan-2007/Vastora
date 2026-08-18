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

/// <summary>
/// §9.44. FulfillmentMethod never reached pricing before this — a Pickup or Digital order was
/// still charged whatever a shipping zone or the business's flat DefaultDeliveryFee resolved to.
/// </summary>
public class PricingServiceFulfillmentTests
{
    private static PricingService Create(Business business) => new(
        new FakeMongoRepository<Product>(),
        new FakeMongoRepository<Order>(),
        new CouponService(new FakeMongoRepository<Coupon>()),
        new PromotionService(new FakeMongoRepository<Promotion>(), new FakeMongoRepository<Order>()),
        new CustomerGroupService(new FakeMongoRepository<CustomerGroup>(), new FakeMongoRepository<AppUser>()),
        new ShippingService(new FakeMongoRepository<ShippingZone>()),
        new TaxService(),
        new GiftCardService(new FakeMongoRepository<GiftCard>(), new FakeMongoRepository<Business>()),
        new StoreCreditService(new FakeMongoRepository<StoreCreditEntry>(), new FakeMongoRepository<Business>()));

    private static ResolvedLine Line(Product product) =>
        new(product, null, product.Id, null, product.Name, null, product.Price, product.CostPrice, 1, 0m);

    [Theory]
    [InlineData(FulfillmentMethod.Delivery, 20)]
    [InlineData(FulfillmentMethod.ExternalCourier, 20)]
    [InlineData(FulfillmentMethod.Pickup, 0)]
    [InlineData(FulfillmentMethod.Digital, 0)]
    public async Task PriceAsync_OnlyChargesDeliveryFee_WhenTheMethodHasADeliveryLeg(FulfillmentMethod method, decimal expectedFee)
    {
        var business = new Business { Id = "biz-1", Currency = "USD", DefaultDeliveryFee = 20m };
        var product = new Product { Id = "p1", BusinessId = "biz-1", Name = "Widget", Price = 50m };

        var breakdown = await Create(business).PriceAsync(
            new PricingContext(business, "cust-1", null, null, [], [], null, null, false, method),
            [Line(product)], CancellationToken.None);

        Assert.Equal(expectedFee, breakdown.DeliveryFee);
        Assert.Equal(expectedFee == 0 ? 50m : 70m, breakdown.Total);
    }

    [Fact]
    public async Task PriceAsync_ReturnsNoShippingOptions_ForPickup()
    {
        var business = new Business { Id = "biz-1", Currency = "USD", DefaultDeliveryFee = 20m };
        var product = new Product { Id = "p1", BusinessId = "biz-1", Name = "Widget", Price = 50m };

        var breakdown = await Create(business).PriceAsync(
            new PricingContext(business, "cust-1", null, null, [], [], null, null, false, FulfillmentMethod.Pickup),
            [Line(product)], CancellationToken.None);

        Assert.Empty(breakdown.ShippingOptions);
        Assert.Null(breakdown.ShippingMethodName);
    }
}

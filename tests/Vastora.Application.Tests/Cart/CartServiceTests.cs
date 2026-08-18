using Vastora.Application.Cart;
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
using CartEntity = Vastora.Domain.Entities.Cart;

namespace Vastora.Application.Tests.Cart;

public class CartServiceTests
{
    private static (ICartService CartService, IGiftCardService GiftCards, IStoreCreditService StoreCredit) Create(
        Business business, Product product, FakeMongoRepository<Coupon>? coupons = null)
    {
        var carts = new FakeMongoRepository<CartEntity>();
        var products = new FakeMongoRepository<Product>();
        products.Seed(product);
        var businesses = new FakeMongoRepository<Business>();
        businesses.Seed(business);

        var couponService = new CouponService(coupons ?? new FakeMongoRepository<Coupon>());
        var promotionService = new PromotionService(new FakeMongoRepository<Promotion>(), new FakeMongoRepository<Order>());
        var customerGroupService = new CustomerGroupService(new FakeMongoRepository<CustomerGroup>(), new FakeMongoRepository<AppUser>());
        var shippingService = new ShippingService(new FakeMongoRepository<ShippingZone>());
        var taxService = new TaxService();
        var giftCardService = new GiftCardService(new FakeMongoRepository<GiftCard>(), businesses);
        var storeCreditService = new StoreCreditService(new FakeMongoRepository<StoreCreditEntry>(), businesses);

        var pricingService = new PricingService(
            products, new FakeMongoRepository<Order>(), couponService, promotionService,
            customerGroupService, shippingService, taxService, giftCardService, storeCreditService);

        var cartService = new CartService(carts, products, businesses, couponService, promotionService, giftCardService, pricingService);

        return (cartService, giftCardService, storeCreditService);
    }

    private static Product SimpleProduct() => new()
    {
        BusinessId = "biz-1",
        CategoryId = "cat-1",
        Name = "Widget",
        Price = 100m,
        Status = ProductStatus.Active
    };

    [Fact]
    public async Task ApplyGiftCardAsync_DiscountsThePreview_WithoutWaitingForCheckout()
    {
        var business = new Business { Id = "biz-1", Currency = "USD" };
        var product = SimpleProduct();
        var (cartService, giftCards, _) = Create(business, product);

        var owner = CartOwner.ForCustomer("cust-1");
        await cartService.AddItemAsync("t1", "biz-1", owner, new AddCartItemRequest(product.Id, 1), CancellationToken.None);

        var issued = await giftCards.IssueAsync("t1", "biz-1", new IssueGiftCardRequest(40m, null, null), CancellationToken.None);

        // §9.43: before this fix, cart.GiftCardCodes was set but CartService.MapAsync always
        // priced with an empty list — a shopper who applied a gift card saw no discount until
        // checkout actually charged them. This proves the preview now agrees with the charge.
        var result = await cartService.ApplyGiftCardAsync("biz-1", owner, new ApplyCartCouponRequest(issued.Code!), CancellationToken.None);

        Assert.Equal(40m, result.GiftCardTotal);
        Assert.Equal(60m, result.AmountDue);
        Assert.Contains(issued.Code!.ToUpperInvariant(), result.GiftCardCodes);
    }

    [Fact]
    public async Task RemoveCouponAsync_ClearsTheAppliedCoupon()
    {
        // §9.45: DELETE .../coupon didn't exist at all — only promotions and gift cards had a
        // matching removal endpoint, so a shopper could apply a coupon but never take it back off
        // short of clearing the whole cart.
        var business = new Business { Id = "biz-1", Currency = "USD" };
        var product = SimpleProduct();
        var coupons = new FakeMongoRepository<Coupon>();
        coupons.Seed(new Coupon
        {
            BusinessId = "biz-1", Code = "SAVE10", DiscountType = DiscountType.Percentage, DiscountValue = 10m,
            StartsAt = DateTime.UtcNow.AddDays(-1), IsActive = true
        });
        var (cartService, _, _) = Create(business, product, coupons);

        var owner = CartOwner.ForCustomer("cust-1");
        await cartService.AddItemAsync("t1", "biz-1", owner, new AddCartItemRequest(product.Id, 1), CancellationToken.None);
        var applied = await cartService.ApplyCouponAsync("biz-1", owner, new ApplyCartCouponRequest("SAVE10"), CancellationToken.None);
        Assert.Equal("SAVE10", applied.CouponCode);
        Assert.Equal(10m, applied.DiscountTotal);

        var result = await cartService.RemoveCouponAsync("biz-1", owner, CancellationToken.None);

        Assert.Null(result.CouponCode);
        Assert.Equal(0m, result.DiscountTotal);
        Assert.Equal(100m, result.AmountDue);
    }

    [Fact]
    public async Task SetUseStoreCreditAsync_DiscountsThePreview_UpToTheAvailableBalance()
    {
        var business = new Business { Id = "biz-1", Currency = "USD" };
        var product = SimpleProduct();
        var (cartService, _, storeCredit) = Create(business, product);

        var owner = CartOwner.ForCustomer("cust-1");
        await cartService.AddItemAsync("t1", "biz-1", owner, new AddCartItemRequest(product.Id, 1), CancellationToken.None);
        await storeCredit.RecordAsync("t1", "biz-1", "cust-1", 30m, StoreCreditReason.RefundToCredit, "refund", ct: CancellationToken.None);

        var result = await cartService.SetUseStoreCreditAsync("biz-1", owner, true, CancellationToken.None);

        Assert.True(result.UseStoreCredit);
        Assert.Equal(30m, result.StoreCreditApplied);
        Assert.Equal(70m, result.AmountDue);
    }

    [Fact]
    public async Task ClearAsync_ResetsUseStoreCredit_AlongsideCouponsAndGiftCards()
    {
        var business = new Business { Id = "biz-1", Currency = "USD" };
        var product = SimpleProduct();
        var (cartService, _, storeCredit) = Create(business, product);

        var owner = CartOwner.ForCustomer("cust-1");
        await cartService.AddItemAsync("t1", "biz-1", owner, new AddCartItemRequest(product.Id, 1), CancellationToken.None);
        await storeCredit.RecordAsync("t1", "biz-1", "cust-1", 30m, StoreCreditReason.RefundToCredit, "refund", ct: CancellationToken.None);
        await cartService.SetUseStoreCreditAsync("biz-1", owner, true, CancellationToken.None);

        await cartService.ClearAsync("biz-1", owner, CancellationToken.None);
        var cleared = await cartService.GetAsync("biz-1", owner, CancellationToken.None);

        Assert.False(cleared.UseStoreCredit);
        Assert.Empty(cleared.GiftCardCodes);
        Assert.Equal(FulfillmentMethod.Delivery, cleared.FulfillmentMethod);
    }

    [Fact]
    public async Task GetAsync_ShowsTheBusinessDefaultDeliveryFee_ByDefault()
    {
        // §9.44: before this fix, CartService.MapAsync passed 0m (not null) as the pricing
        // call's ExplicitDeliveryFee, which ResolveFeeAsync treats as "the fee is explicitly
        // zero" — the real DefaultDeliveryFee was never even consulted, and the cart response had
        // no delivery-fee field to show it on regardless.
        var business = new Business { Id = "biz-1", Currency = "USD", DefaultDeliveryFee = 15m };
        var product = SimpleProduct();
        var (cartService, _, _) = Create(business, product);

        var owner = CartOwner.ForCustomer("cust-1");
        await cartService.AddItemAsync("t1", "biz-1", owner, new AddCartItemRequest(product.Id, 1), CancellationToken.None);

        var result = await cartService.GetAsync("biz-1", owner, CancellationToken.None);

        Assert.Equal(FulfillmentMethod.Delivery, result.FulfillmentMethod);
        Assert.Equal(15m, result.DeliveryFee);
        Assert.Equal(115m, result.AmountDue);
    }

    [Fact]
    public async Task SetFulfillmentMethodAsync_Pickup_DropsTheDeliveryFeeEntirely()
    {
        var business = new Business { Id = "biz-1", Currency = "USD", DefaultDeliveryFee = 15m };
        var product = SimpleProduct();
        var (cartService, _, _) = Create(business, product);

        var owner = CartOwner.ForCustomer("cust-1");
        await cartService.AddItemAsync("t1", "biz-1", owner, new AddCartItemRequest(product.Id, 1), CancellationToken.None);

        // Confirm delivery applies first, so the Pickup assertion below is a real transition,
        // not just "it was already zero".
        var beforePickup = await cartService.GetAsync("biz-1", owner, CancellationToken.None);
        Assert.Equal(15m, beforePickup.DeliveryFee);

        var result = await cartService.SetFulfillmentMethodAsync("biz-1", owner, FulfillmentMethod.Pickup, CancellationToken.None);

        Assert.Equal(FulfillmentMethod.Pickup, result.FulfillmentMethod);
        Assert.Equal(0m, result.DeliveryFee);
        Assert.Null(result.ShippingMethodName);
        Assert.Empty(result.ShippingOptions);
        Assert.Equal(100m, result.AmountDue);
    }
}

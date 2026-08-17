using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Coupons;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.CustomerGroups;
using Vastora.Application.GiftCards;
using Vastora.Application.Inventory;
using Vastora.Application.Orders;
using Vastora.Application.Pricing;
using Vastora.Application.Products;
using Vastora.Application.Promotions;
using Vastora.Application.Shipping;
using Vastora.Application.Tax;
using Vastora.Application.Tenants;
using Vastora.Application.Webhooks;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Orders;

public class OrderServiceTests
{
    /// <summary>
    /// Wires the real pricing pipeline rather than mocking IPricingService, because checkout's
    /// behaviour *is* the interaction between pricing, stock and settlement — a stubbed price
    /// would make these tests assert that the plumbing was called, not that the answer is right.
    /// Only the outbound edges (coupons, notifications, webhooks) are mocked.
    /// </summary>
    private static (OrderService Service, FakeMongoRepository<Order> Orders, FakeMongoRepository<Domain.Entities.Cart> Carts,
        FakeMongoRepository<Product> Products, FakeMongoRepository<Business> Businesses,
        FakeMongoRepository<DeliveryAgentProfile> Profiles, FakeMongoRepository<AppUser> Users,
        FakeMongoRepository<LedgerEntry> LedgerEntries) Create()
    {
        var orders = new FakeMongoRepository<Order>();
        var carts = new FakeMongoRepository<Domain.Entities.Cart>();
        var products = new FakeMongoRepository<Product>();
        var businesses = new FakeMongoRepository<Business>();
        var profiles = new FakeMongoRepository<DeliveryAgentProfile>();
        var users = new FakeMongoRepository<AppUser>();
        var ledgerEntries = new FakeMongoRepository<LedgerEntry>();

        var coupons = new Mock<ICouponService>().Object;
        var notifications = new Mock<INotificationService>().Object;
        var webhooks = new Mock<IWebhookPublisher>().Object;

        var tenants = new FakeMongoRepository<TenantAccount>();
        var productService = new ProductService(products, tenants, new FakeMongoRepository<Category>());
        var stockStore = new FakeProductStockStore(products);
        var inventoryService = new InventoryService(products, new FakeMongoRepository<StockMovement>(), stockStore, productService);

        var promotionService = new PromotionService(new FakeMongoRepository<Promotion>(), orders);
        var customerGroups = new CustomerGroupService(new FakeMongoRepository<CustomerGroup>(), users);
        var shipping = new ShippingService(new FakeMongoRepository<ShippingZone>());
        var giftCards = new GiftCardService(new FakeMongoRepository<GiftCard>(), businesses);
        var storeCredit = new StoreCreditService(new FakeMongoRepository<StoreCreditEntry>(), businesses);

        var pricing = new PricingService(
            products, orders, coupons, promotionService, customerGroups,
            shipping, new TaxService(), giftCards, storeCredit);

        var service = new OrderService(
            orders, carts, products, businesses, profiles, users, ledgerEntries,
            coupons, pricing, promotionService, giftCards, storeCredit,
            notifications, inventoryService, webhooks, NullLogger<OrderService>.Instance);

        return (service, orders, carts, products, businesses, profiles, users, ledgerEntries);
    }

    /// <summary>An Active product inside its publish window — the minimum to be purchasable (§9.28).</summary>
    private static Product SellableProduct(string businessId, decimal price, int stock, bool trackInventory = true) => new()
    {
        BusinessId = businessId,
        Name = "Widget",
        Price = price,
        StockQuantity = stock,
        TrackInventory = trackInventory,
        Status = ProductStatus.Active
    };

    private static Address TestAddress => new() { Label = "Home", Line1 = "Line1", City = "City", Phone = "0123456789" };

    [Fact]
    public async Task UpdateStatusAsync_LegalTransition_Succeeds()
    {
        var (service, orders, _, _, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business())[0];
        var order = orders.Seed(new Order { TenantId = "t1", BusinessId = business.Id, Status = OrderStatus.Processing })[0];

        var result = await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Confirmed, "Confirmed by staff"), CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, result.Status);
        Assert.Contains(result.StatusHistory, e => e.Status == OrderStatus.Confirmed);
    }

    [Theory]
    [InlineData(OrderStatus.Processing, OrderStatus.Delivered)]
    [InlineData(OrderStatus.Delivered, OrderStatus.Processing)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Processing)]
    public async Task UpdateStatusAsync_IllegalTransition_Throws(OrderStatus from, OrderStatus to)
    {
        var (service, orders, _, _, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business())[0];
        var order = orders.Seed(new Order { TenantId = "t1", BusinessId = business.Id, Status = from })[0];

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.UpdateStatusAsync("t1", business.Id, order.Id, new UpdateOrderStatusRequest(to, ""), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateStatusAsync_SameStatus_IsANoOpNotAnError()
    {
        var (service, orders, _, _, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business())[0];
        var order = orders.Seed(new Order { TenantId = "t1", BusinessId = business.Id, Status = OrderStatus.Processing })[0];

        var result = await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Processing, "re-noting"), CancellationToken.None);

        Assert.Equal(OrderStatus.Processing, result.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToDelivered_CreditsAssignedAgentBalance()
    {
        var (service, orders, _, _, businesses, profiles, _, _) = Create();
        var business = businesses.Seed(new Business())[0];
        var agentProfile = profiles.Seed(new DeliveryAgentProfile
        {
            BusinessId = business.Id, UserId = "agent-1", DeliveryCharge = 25m, CompletedDeliveries = 2, Balance = 100m
        })[0];
        var order = orders.Seed(new Order
        {
            TenantId = "t1", BusinessId = business.Id, Status = OrderStatus.OutForDelivery, DeliveryAgentUserId = "agent-1"
        })[0];

        await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Delivered, "Delivered"), CancellationToken.None);

        var updatedProfile = await profiles.GetByIdAsync(agentProfile.Id, CancellationToken.None);
        Assert.Equal(125m, updatedProfile!.Balance);
        Assert.Equal(3, updatedProfile.CompletedDeliveries);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToCancelled_RestocksTrackedProducts()
    {
        var (service, orders, _, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business())[0];
        var product = products.Seed(new Product { BusinessId = business.Id, TrackInventory = true, StockQuantity = 5 })[0];
        var order = orders.Seed(new Order
        {
            TenantId = "t1",
            BusinessId = business.Id,
            Status = OrderStatus.Processing,
            Items = [new OrderItem { ProductId = product.Id, ProductName = "P", UnitPrice = 10, Quantity = 3 }]
        })[0];

        await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Cancelled, "Out of stock elsewhere"), CancellationToken.None);

        var updatedProduct = await products.GetByIdAsync(product.Id, CancellationToken.None);
        Assert.Equal(8, updatedProduct!.StockQuantity);
    }

    [Fact]
    public async Task AssignDeliveryAgentAsync_RejectedWhenDeliveryModuleDisabled()
    {
        var (service, orders, _, _, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { DeliveryModuleEnabled = false })[0];
        var order = orders.Seed(new Order { TenantId = "t1", BusinessId = business.Id, Status = OrderStatus.Processing })[0];

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.AssignDeliveryAgentAsync("t1", business.Id, order.Id, new AssignDeliveryAgentRequest("agent-1"), CancellationToken.None));
    }

    [Fact]
    public async Task CheckoutAsync_NoDeliveryFeeSupplied_FallsBackToBusinessDefault()
    {
        var (service, _, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { DefaultDeliveryFee = 15m, Currency = "USD" })[0];
        var product = products.Seed(SellableProduct(business.Id, 20m, 0, trackInventory: false))[0];
        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "P", UnitPrice = 20m, Quantity = 1 }]
        });

        var result = await service.CheckoutAsync("t1", business.Id, "cust-1", null,
            new CheckoutRequest(TestAddress, null), CancellationToken.None);

        Assert.Equal(15m, result.DeliveryFee);
        Assert.Equal(35m, result.Total);
        // §9.38 — the currency is snapshotted, so a later Business change can't reinterpret it.
        Assert.Equal("USD", result.Currency);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToDelivered_WritesRevenueAndDeliveryPayoutLedgerEntries()
    {
        var (service, orders, _, _, businesses, profiles, _, ledgerEntries) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        profiles.Seed(new DeliveryAgentProfile { BusinessId = business.Id, UserId = "agent-1", DeliveryCharge = 25m });
        var order = orders.Seed(new Order
        {
            TenantId = "t1", BusinessId = business.Id, Status = OrderStatus.OutForDelivery,
            DeliveryAgentUserId = "agent-1", Total = 150m
        })[0];

        await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Delivered, "Delivered"), CancellationToken.None);

        var entries = await ledgerEntries.FindAsync(l => l.BusinessId == business.Id, CancellationToken.None);
        Assert.Contains(entries, e => e.Type == LedgerEntryType.Revenue && e.Amount == 150m);
        Assert.Contains(entries, e => e.Type == LedgerEntryType.DeliveryPayout && e.Amount == 25m);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToRefunded_WritesRefundLedgerEntry()
    {
        var (service, orders, _, _, businesses, _, _, ledgerEntries) = Create();
        var business = businesses.Seed(new Business())[0];
        var order = orders.Seed(new Order { TenantId = "t1", BusinessId = business.Id, Status = OrderStatus.Delivered, Total = 80m })[0];

        await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Refunded, "Customer returned item"), CancellationToken.None);

        var entry = Assert.Single(await ledgerEntries.FindAsync(l => l.BusinessId == business.Id, CancellationToken.None));
        Assert.Equal(LedgerEntryType.Refund, entry.Type);
        Assert.Equal(80m, entry.Amount);
    }

    // -----------------------------------------------------------------------------------
    // §9.17 — checkout integrity. These are the regression tests for the defect the audit
    // found: stock was deducted inside the item loop, before the order existed, with no
    // rollback, so a failure partway through left earlier lines permanently deducted.
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task CheckoutAsync_WhenALaterLineIsOutOfStock_RestoresEveryLineAlreadyDeducted()
    {
        var (service, orders, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];

        // Seeded in one call and indexed positionally: FakeMongoRepository.Seed returns the whole
        // backing list, so a second Seed(...)[0] would hand back the *first* product, not the new one.
        var seeded = products.Seed(
            SellableProduct(business.Id, 10m, 50),
            SellableProduct(business.Id, 10m, 1));

        var plentiful = seeded[0];
        var scarce = seeded[1];

        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items =
            [
                new CartItem { ProductId = plentiful.Id, ProductName = "Plentiful", UnitPrice = 10m, Quantity = 2 },
                new CartItem { ProductId = scarce.Id, ProductName = "Scarce", UnitPrice = 10m, Quantity = 5 }
            ]
        });

        await Assert.ThrowsAsync<ConflictException>(() => service.CheckoutAsync(
            "t1", business.Id, "cust-1", null, new CheckoutRequest(TestAddress, null), CancellationToken.None));

        // The first line's stock must be back exactly where it started. Before §9.17 it stayed
        // at 48 forever, with no order to account for the missing two.
        Assert.Equal(50, (await products.GetByIdAsync(plentiful.Id, CancellationToken.None))!.StockQuantity);
        Assert.Equal(1, (await products.GetByIdAsync(scarce.Id, CancellationToken.None))!.StockQuantity);
        Assert.Empty(await orders.FindAsync(o => o.BusinessId == business.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CheckoutAsync_RefusesToOversell_WhenTheCartWantsMoreThanExists()
    {
        var (service, _, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(SellableProduct(business.Id, 10m, 3))[0];

        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "Widget", UnitPrice = 10m, Quantity = 4 }]
        });

        await Assert.ThrowsAsync<ConflictException>(() => service.CheckoutAsync(
            "t1", business.Id, "cust-1", null, new CheckoutRequest(TestAddress, null), CancellationToken.None));

        Assert.Equal(3, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
    }

    [Fact]
    public async Task CheckoutAsync_RejectsAProductThatIsNoLongerPublished()
    {
        var (service, _, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];

        var retired = SellableProduct(business.Id, 10m, 20);
        retired.UnpublishedAt = DateTime.UtcNow.AddDays(-1);
        var product = products.Seed(retired)[0];

        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "Widget", UnitPrice = 10m, Quantity = 1 }]
        });

        await Assert.ThrowsAsync<ConflictException>(() => service.CheckoutAsync(
            "t1", business.Id, "cust-1", null, new CheckoutRequest(TestAddress, null), CancellationToken.None));

        // Validation happens before any stock moves, so nothing needs unwinding here at all.
        Assert.Equal(20, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
    }

    [Fact]
    public async Task CheckoutAsync_SnapshotsUnitCost_SoLaterCostEditsCannotRewriteHistoricalMargin()
    {
        var (service, orders, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];

        var sellable = SellableProduct(business.Id, 50m, 10);
        sellable.CostPrice = 20m;
        var product = products.Seed(sellable)[0];

        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "Widget", UnitPrice = 50m, Quantity = 2 }]
        });

        var placed = await service.CheckoutAsync("t1", business.Id, "cust-1", null,
            new CheckoutRequest(TestAddress, 0m), CancellationToken.None);

        // The supplier puts their price up after the sale.
        product.CostPrice = 45m;
        await products.UpdateAsync(product, CancellationToken.None);

        var stored = (await orders.GetByIdAsync(placed.Id, CancellationToken.None))!;

        // The order still reports the cost that applied when it was sold — same reasoning as
        // UnitPrice, and what keeps a closed period's margin from moving under it (§9.31).
        Assert.Equal(20m, stored.Items[0].UnitCost);
        Assert.Equal(40m, stored.Items[0].LineCost);
    }

    // -----------------------------------------------------------------------------------
    // §9.19 / §9.31 — tax and COGS at the point of delivery.
    // -----------------------------------------------------------------------------------

    [Fact]
    public async Task CheckoutAsync_AddsTaxOnTop_WhenPricesAreTaxExclusive()
    {
        var (service, _, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business
        {
            Currency = "USD",
            Tax = new TaxSettings { Enabled = true, DefaultRatePercent = 10m, PricesIncludeTax = false }
        })[0];

        var product = products.Seed(SellableProduct(business.Id, 100m, 5))[0];
        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "Widget", UnitPrice = 100m, Quantity = 1 }]
        });

        var result = await service.CheckoutAsync("t1", business.Id, "cust-1", null,
            new CheckoutRequest(TestAddress, 0m), CancellationToken.None);

        Assert.Equal(10m, result.TaxAmount);
        Assert.Equal(110m, result.Total);
    }

    [Fact]
    public async Task CheckoutAsync_ExtractsTaxFromTheTotal_WhenPricesAlreadyIncludeIt()
    {
        var (service, _, carts, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business
        {
            Currency = "USD",
            Tax = new TaxSettings { Enabled = true, DefaultRatePercent = 10m, PricesIncludeTax = true }
        })[0];

        var product = products.Seed(SellableProduct(business.Id, 110m, 5))[0];
        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "Widget", UnitPrice = 110m, Quantity = 1 }]
        });

        var result = await service.CheckoutAsync("t1", business.Id, "cust-1", null,
            new CheckoutRequest(TestAddress, 0m), CancellationToken.None);

        // Getting this backwards would charge 121 instead of 110 — overcharging every customer
        // of every VAT-convention business, which is why the flag is snapshotted on the order.
        Assert.Equal(10m, result.TaxAmount);
        Assert.Equal(110m, result.Total);
        Assert.True(result.PricesIncludeTax);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToDelivered_SplitsRevenueFromCogsAndTax()
    {
        var (service, orders, _, _, businesses, _, _, ledgerEntries) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];

        var order = orders.Seed(new Order
        {
            TenantId = "t1",
            BusinessId = business.Id,
            Status = OrderStatus.OutForDelivery,
            Currency = "USD",
            Total = 110m,
            TaxAmount = 10m,
            Items = [new OrderItem { ProductId = "p1", ProductName = "Widget", UnitPrice = 100m, UnitCost = 60m, Quantity = 1 }]
        })[0];

        await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Delivered, "Delivered"), CancellationToken.None);

        var entries = await ledgerEntries.FindAsync(l => l.BusinessId == business.Id, CancellationToken.None);

        // Three lines, not one. Tax is a liability owed onward, not earnings, so it is excluded
        // from Revenue; COGS is what makes gross margin computable at all (§9.31).
        Assert.Contains(entries, e => e.Type == LedgerEntryType.Revenue && e.Amount == 100m);
        Assert.Contains(entries, e => e.Type == LedgerEntryType.CostOfGoodsSold && e.Amount == 60m);
        Assert.Contains(entries, e => e.Type == LedgerEntryType.TaxCollected && e.Amount == 10m);
    }

    [Fact]
    public async Task UpdateStatusAsync_ToRefunded_PutsTheGoodsBackIntoStock()
    {
        var (service, orders, _, products, businesses, _, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(SellableProduct(business.Id, 10m, 5))[0];

        var order = orders.Seed(new Order
        {
            TenantId = "t1",
            BusinessId = business.Id,
            Status = OrderStatus.Delivered,
            Currency = "USD",
            Total = 30m,
            Items = [new OrderItem { ProductId = product.Id, ProductName = "Widget", UnitPrice = 10m, Quantity = 3 }]
        })[0];

        await service.UpdateStatusAsync("t1", business.Id, order.Id,
            new UpdateOrderStatusRequest(OrderStatus.Refunded, "Refunded"), CancellationToken.None);

        // §9.21: the → Refunded transition used to write a ledger entry and leave inventory
        // untouched, so a refunded item silently vanished from stock.
        Assert.Equal(8, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
    }
}

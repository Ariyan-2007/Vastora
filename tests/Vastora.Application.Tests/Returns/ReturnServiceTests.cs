using Moq;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.GiftCards;
using Vastora.Application.Inventory;
using Vastora.Application.Products;
using Vastora.Application.Returns;
using Vastora.Application.Tax;
using Vastora.Application.Tenants;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Application.Webhooks;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Returns;

public class ReturnServiceTests
{
    private static (ReturnService Service, FakeMongoRepository<ReturnRequest> Returns, FakeMongoRepository<Order> Orders,
        FakeMongoRepository<Product> Products, FakeMongoRepository<Business> Businesses,
        FakeMongoRepository<LedgerEntry> Ledger, FakeMongoRepository<StoreCreditEntry> Credit) Create()
    {
        var returns = new FakeMongoRepository<ReturnRequest>();
        var orders = new FakeMongoRepository<Order>();
        var products = new FakeMongoRepository<Product>();
        var businesses = new FakeMongoRepository<Business>();
        var ledger = new FakeMongoRepository<LedgerEntry>();
        var credit = new FakeMongoRepository<StoreCreditEntry>();

        var productService = new ProductService(products, new FakeMongoRepository<TenantAccount>(), new FakeMongoRepository<Category>());
        var stockStore = new FakeProductStockStore(products);
        var inventory = new InventoryService(products, new FakeMongoRepository<StockMovement>(), stockStore, productService);

        var service = new ReturnService(
            returns, orders, businesses, products, ledger, inventory,
            new StoreCreditService(credit, businesses),
            new GiftCardService(new FakeMongoRepository<GiftCard>(), businesses),
            new TaxService(),
            new Mock<INotificationService>().Object,
            new Mock<IWebhookPublisher>().Object);

        return (service, returns, orders, products, businesses, ledger, credit);
    }

    /// <summary>A delivered order with one line of the given quantity, inside the return window.</summary>
    private static Order DeliveredOrder(string businessId, string productId, int quantity, decimal unitPrice) => new()
    {
        TenantId = "t1",
        BusinessId = businessId,
        OrderNumber = "ORD-1",
        CustomerUserId = "cust-1",
        Status = OrderStatus.Delivered,
        Currency = "USD",
        Total = unitPrice * quantity,
        Items = [new OrderItem { ProductId = productId, ProductName = "Widget", UnitPrice = unitPrice, Quantity = quantity }],
        StatusHistory = [new OrderStatusEvent { Status = OrderStatus.Delivered, Timestamp = DateTime.UtcNow.AddDays(-1) }]
    };

    [Fact]
    public async Task RequestAsync_RejectsAnOrderOutsideTheReturnWindow()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD", ReturnWindowDays = 7 })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5 })[0];

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.StatusHistory = [new OrderStatusEvent { Status = OrderStatus.Delivered, Timestamp = DateTime.UtcNow.AddDays(-30) }];
        orders.Seed(order);

        await Assert.ThrowsAsync<ConflictException>(() => service.RequestAsync(
            "t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_RefusesToReturnMoreThanWasBought()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5 })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 2, 50m))[0];

        await Assert.ThrowsAsync<ConflictException>(() => service.RequestAsync(
            "t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 5)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_PricesTheRefundFromTheOrdersOwnSnapshot()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, Price = 999m, StockQuantity = 5 })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 3, 50m))[0];

        var result = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 2)], ReturnReason.SizeOrFit, "Too small", ReturnResolution.Refund),
            CancellationToken.None);

        // 2 × 50 (what was paid), never 2 × 999 (what it costs today).
        Assert.Equal(100m, result.RequestedRefundAmount);
        Assert.Equal(ReturnStatus.Requested, result.Status);
    }

    [Fact]
    public async Task RequestAsync_AcceptsAPickedUpOrder_SameAsADeliveredOne()
    {
        // §9.47: a Pickup order reaches PickedUp, never Delivered — return eligibility must
        // recognise both as "this order actually finished," or a customer who picked their
        // order up in-store could never return anything.
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD", ReturnWindowDays = 7 })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5 })[0];

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Status = OrderStatus.PickedUp;
        order.FulfillmentMethod = FulfillmentMethod.Pickup;
        order.StatusHistory = [new OrderStatusEvent { Status = OrderStatus.PickedUp, Timestamp = DateTime.UtcNow.AddDays(-1) }];
        orders.Seed(order);

        var result = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None);

        Assert.Equal(ReturnStatus.Requested, result.Status);
    }

    [Fact]
    public async Task MarkReceivedAsync_RestocksTheGoods_ButNotBeforeTheyArrive()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 3, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 2)], ReturnReason.SizeOrFit, "", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, "Approved"), "staff-1", CancellationToken.None);

        // Approval alone must not restock — the goods are still with the customer.
        Assert.Equal(5, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);

        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        Assert.Equal(7, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
    }

    [Fact]
    public async Task MarkReceivedAsync_DoesNotRestockDamagedGoods()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 1, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.Damaged, "Arrived broken", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        // Damaged goods come back but are not sellable.
        Assert.Equal(5, (await products.GetByIdAsync(product.Id, CancellationToken.None))!.StockQuantity);
    }

    [Fact]
    public async Task RefundAsync_WritesAPartialRefundAndLeavesThePartlyKeptOrderDelivered()
    {
        var (service, _, orders, products, businesses, ledger, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 3, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.SizeOrFit, "", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);
        var refunded = await service.RefundAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        Assert.Equal(ReturnStatus.Refunded, refunded.Status);

        var entry = Assert.Single(await ledger.FindAsync(l => l.Type == LedgerEntryType.Refund, CancellationToken.None));
        Assert.Equal(50m, entry.Amount); // one line of three — not the whole order total

        var updatedOrder = (await orders.GetByIdAsync(order.Id, CancellationToken.None))!;
        Assert.Equal(50m, updatedOrder.RefundedAmount);
        Assert.Equal(1, updatedOrder.Items[0].RefundedQuantity);
        // Still Delivered: it is, for the two items the customer kept.
        Assert.Equal(OrderStatus.Delivered, updatedOrder.Status);
    }

    /// <summary>
    /// §9.48. The two lines a return used to leave untouched: the returned unit's cost stayed
    /// permanently expensed even though RestockAsync puts it straight back into sellable
    /// inventory (so selling it again would COGS the same physical unit twice), and its tax
    /// stayed on the books as owed even after the customer got that portion back.
    /// </summary>
    [Fact]
    public async Task RefundAsync_ReversesTheReturnedLinesCostAndTax()
    {
        var (service, _, orders, products, businesses, ledger, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];

        var order = DeliveredOrder(business.Id, product.Id, 3, 100m);
        order.TaxAmount = 30m;
        order.TaxRatePercent = 10m;
        order.Items[0].UnitCost = 60m;
        orders.Seed(order);

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.SizeOrFit, "", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);
        await service.RefundAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        var entries = await ledger.FindAsync(l => l.BusinessId == business.Id, CancellationToken.None);

        // One line of three: 100 refunded, of which 10 (10%) is tax — Refund books the other 90.
        Assert.Contains(entries, e => e.Type == LedgerEntryType.Refund && e.Amount == 90m);
        Assert.Contains(entries, e => e.Type == LedgerEntryType.TaxCollected && e.Amount == -10m);
        Assert.Contains(entries, e => e.Type == LedgerEntryType.CostOfGoodsSold && e.Amount == -60m);
    }

    [Fact]
    public async Task RefundAsync_MarksTheOrderRefunded_OnceEveryLineHasComeBack()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 2, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 2)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);
        await service.RefundAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        Assert.Equal(OrderStatus.Refunded, (await orders.GetByIdAsync(order.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task RefundAsync_SettlesToStoreCredit_WhenThatIsTheChosenResolution()
    {
        var (service, _, orders, products, businesses, _, credit) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 1, 80m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.ChangedMind, "", ReturnResolution.StoreCredit),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);
        await service.RefundAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        var entry = Assert.Single(await credit.FindAsync(c => c.CustomerUserId == "cust-1", CancellationToken.None));
        Assert.Equal(80m, entry.Amount);
        Assert.Equal(StoreCreditReason.RefundToCredit, entry.Reason);
    }

    [Fact]
    public async Task DecideAsync_RefusesToApproveMoreThanWasRequested()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5 })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 1, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() => service.DecideAsync(
            "t1", business.Id, rma.Id, new DecideReturnRequest(true, 500m, ""), "staff-1", CancellationToken.None));
    }

    [Fact]
    public async Task RefundAsync_RefusesUntilTheGoodsHaveBeenReceived()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5 })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 1, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RefundAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None));
    }

    // -----------------------------------------------------------------------------------
    // §9.49 — Exchange. Same-price variant swap only: no payment gateway exists to collect
    // a shortfall or settle an overage, so RequestAsync validates the desired variant prices
    // identically to what was already paid, and ExchangeAsync moves no money at all.
    // -----------------------------------------------------------------------------------

    private static (ProductVariant Small, ProductVariant Large, Product Product) VariantProduct(
        string businessId, FakeMongoRepository<Product> products, decimal price, decimal? largePriceOverride = null)
    {
        var small = new ProductVariant { Id = "v-small", AttributeSummary = "Small", Sku = "SM", StockQuantity = 5 };
        var large = new ProductVariant { Id = "v-large", AttributeSummary = "Large", Sku = "LG", StockQuantity = 5, PriceOverride = largePriceOverride };
        var product = products.Seed(new Product { BusinessId = businessId, Price = price, TrackInventory = true, Variants = [small, large] })[0];
        return (small, large, product);
    }

    [Fact]
    public async Task RequestAsync_Exchange_AcceptsASamePriceVariantSwap()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, large, product) = VariantProduct(business.Id, products, 50m);

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        var result = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1, large.Id)],
                ReturnReason.SizeOrFit, "Too small", ReturnResolution.Exchange),
            CancellationToken.None);

        Assert.Equal(ReturnResolution.Exchange, result.Resolution);
        Assert.Equal(large.Id, result.Items[0].DesiredVariantId);
        Assert.Equal("Large", result.Items[0].DesiredVariantSummary);
    }

    [Fact]
    public async Task RequestAsync_Exchange_RejectsAVariantThatWouldChangeThePrice()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, large, product) = VariantProduct(business.Id, products, 50m, largePriceOverride: 60m);

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        await Assert.ThrowsAsync<ConflictException>(() => service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1, large.Id)],
                ReturnReason.SizeOrFit, "", ReturnResolution.Exchange),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_Exchange_RequiresADesiredVariant()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, _, product) = VariantProduct(business.Id, products, 50m);

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        await Assert.ThrowsAsync<ConflictException>(() => service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1)],
                ReturnReason.SizeOrFit, "", ReturnResolution.Exchange),
            CancellationToken.None));
    }

    [Fact]
    public async Task RequestAsync_NonExchange_RejectsADesiredVariant()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, large, product) = VariantProduct(business.Id, products, 50m);

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        await Assert.ThrowsAsync<ConflictException>(() => service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1, large.Id)],
                ReturnReason.SizeOrFit, "", ReturnResolution.Refund),
            CancellationToken.None));
    }

    /// <summary>
    /// The full happy path: ships the desired variant, restocks the original (MarkReceivedAsync's
    /// existing behaviour, unaffected by Resolution), swaps the order's own line, and — the point
    /// of the whole feature — moves no money at all.
    /// </summary>
    [Fact]
    public async Task ExchangeAsync_ShipsTheDesiredVariant_RestocksTheOriginal_AndSwapsTheOrderLine()
    {
        var (service, _, orders, products, businesses, ledger, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, large, product) = VariantProduct(business.Id, products, 50m);

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1, large.Id)],
                ReturnReason.SizeOrFit, "", ReturnResolution.Exchange),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);
        var result = await service.ExchangeAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        Assert.Equal(ReturnStatus.Exchanged, result.Status);
        Assert.True(result.Exchanged);

        var updatedProduct = (await products.GetByIdAsync(product.Id, CancellationToken.None))!;
        Assert.Equal(6, updatedProduct.Variants.First(v => v.Id == small.Id).StockQuantity); // 5 + 1 restocked
        Assert.Equal(4, updatedProduct.Variants.First(v => v.Id == large.Id).StockQuantity); // 5 - 1 shipped

        var updatedOrder = (await orders.GetByIdAsync(order.Id, CancellationToken.None))!;
        Assert.Equal(large.Id, updatedOrder.Items[0].VariantId);
        // No money moved: RefundedAmount/RefundedQuantity are untouched, and no ledger entry exists.
        Assert.Equal(0m, updatedOrder.RefundedAmount);
        Assert.Equal(0, updatedOrder.Items[0].RefundedQuantity);
        Assert.Empty(await ledger.FindAsync(l => l.BusinessId == business.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ExchangeAsync_PartialQuantity_SplitsTheOrderLine()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, large, product) = VariantProduct(business.Id, products, 50m);

        var order = DeliveredOrder(business.Id, product.Id, 3, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1, large.Id)],
                ReturnReason.SizeOrFit, "", ReturnResolution.Exchange),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);
        await service.ExchangeAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        var updatedOrder = (await orders.GetByIdAsync(order.Id, CancellationToken.None))!;
        // Two kept in Small, one now in Large — a new line rather than mutating the original.
        Assert.Equal(2, updatedOrder.Items.Single(i => i.VariantId == small.Id).Quantity);
        Assert.Equal(1, updatedOrder.Items.Single(i => i.VariantId == large.Id).Quantity);
    }

    [Fact]
    public async Task RefundAsync_RejectsAnExchangeResolutionReturn()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var (small, large, product) = VariantProduct(business.Id, products, 50m);

        var order = DeliveredOrder(business.Id, product.Id, 1, 50m);
        order.Items[0].VariantId = small.Id;
        orders.Seed(order);

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, small.Id, 1, large.Id)],
                ReturnReason.SizeOrFit, "", ReturnResolution.Exchange),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.RefundAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None));
    }

    [Fact]
    public async Task ExchangeAsync_RejectsANonExchangeResolutionReturn()
    {
        var (service, _, orders, products, businesses, _, _) = Create();
        var business = businesses.Seed(new Business { Currency = "USD" })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, StockQuantity = 5, TrackInventory = true })[0];
        var order = orders.Seed(DeliveredOrder(business.Id, product.Id, 1, 50m))[0];

        var rma = await service.RequestAsync("t1", business.Id, "cust-1",
            new CreateReturnRequest(order.Id, [new ReturnLineRequest(product.Id, null, 1)], ReturnReason.ChangedMind, "", ReturnResolution.Refund),
            CancellationToken.None);

        await service.DecideAsync("t1", business.Id, rma.Id, new DecideReturnRequest(true, null, ""), "staff-1", CancellationToken.None);
        await service.MarkReceivedAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None);

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.ExchangeAsync("t1", business.Id, rma.Id, "staff-1", CancellationToken.None));
    }
}

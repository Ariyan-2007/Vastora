using Moq;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.GiftCards;
using Vastora.Application.Inventory;
using Vastora.Application.Products;
using Vastora.Application.Returns;
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

        var productService = new ProductService(products, new FakeMongoRepository<TenantAccount>());
        var stockStore = new FakeProductStockStore(products);
        var inventory = new InventoryService(products, new FakeMongoRepository<StockMovement>(), stockStore, productService);

        var service = new ReturnService(
            returns, orders, businesses, ledger, inventory,
            new StoreCreditService(credit, businesses),
            new GiftCardService(new FakeMongoRepository<GiftCard>(), businesses),
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
}

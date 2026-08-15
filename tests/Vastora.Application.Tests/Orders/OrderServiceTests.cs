using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Coupons;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Inventory;
using Vastora.Application.Orders;
using Vastora.Application.Products;
using Vastora.Application.Tenants;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Orders;

public class OrderServiceTests
{
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

        var tenants = new FakeMongoRepository<TenantAccount>();
        var productService = new ProductService(products, tenants);
        var inventoryService = new InventoryService(products, new FakeMongoRepository<StockMovement>(), productService);

        var service = new OrderService(orders, carts, products, businesses, profiles, users, ledgerEntries, coupons, notifications, inventoryService, NullLogger<OrderService>.Instance);
        return (service, orders, carts, products, businesses, profiles, users, ledgerEntries);
    }

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
        var business = businesses.Seed(new Business { DefaultDeliveryFee = 15m })[0];
        var product = products.Seed(new Product { BusinessId = business.Id, Price = 20m, TrackInventory = false })[0];
        carts.Seed(new Domain.Entities.Cart
        {
            BusinessId = business.Id,
            CustomerUserId = "cust-1",
            Items = [new CartItem { ProductId = product.Id, ProductName = "P", UnitPrice = 20m, Quantity = 1 }]
        });

        var address = new Address { Label = "Home", Line1 = "Line1", City = "City", Phone = "0123456789" };
        var result = await service.CheckoutAsync("t1", business.Id, "cust-1",
            new CheckoutRequest(address, null), CancellationToken.None);

        Assert.Equal(15m, result.DeliveryFee);
        Assert.Equal(35m, result.Total);
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
}

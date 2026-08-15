using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Orders;

public class OrderService(
    IMongoRepository<Order> orders,
    IMongoRepository<Domain.Entities.Cart> carts,
    IMongoRepository<Product> products,
    IMongoRepository<Business> businesses,
    ICouponService couponService) : IOrderService
{
    private static readonly HashSet<OrderStatus> CancellableStatuses = [OrderStatus.PendingPayment, OrderStatus.Processing, OrderStatus.Confirmed];

    public async Task<OrderResponse> CheckoutAsync(string tenantId, string businessId, string customerUserId, CheckoutRequest request, CancellationToken ct = default)
    {
        var cart = await carts.FindOneAsync(c => c.BusinessId == businessId && c.CustomerUserId == customerUserId, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            throw new ConflictException("Your cart is empty.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var cartItem in cart.Items)
        {
            var product = await products.GetByIdAsync(cartItem.ProductId, ct)
                ?? throw new NotFoundException(nameof(Product), cartItem.ProductId);

            if (product.TrackInventory && product.StockQuantity < cartItem.Quantity)
            {
                throw new ConflictException($"'{product.Name}' only has {product.StockQuantity} left in stock.");
            }

            orderItems.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.EffectivePrice,
                Quantity = cartItem.Quantity
            });

            if (product.TrackInventory)
            {
                product.StockQuantity -= cartItem.Quantity;
                product.UpdatedAt = DateTime.UtcNow;
                await products.UpdateAsync(product, ct);
            }
        }

        var subtotal = orderItems.Sum(i => i.LineTotal);
        var discount = 0m;
        if (!string.IsNullOrWhiteSpace(cart.CouponCode))
        {
            discount = await couponService.ValidateAndPriceAsync(businessId, cart.CouponCode, subtotal, ct);
            await couponService.RegisterUsageAsync(businessId, cart.CouponCode, ct);
        }

        var order = new Order
        {
            TenantId = tenantId,
            BusinessId = businessId,
            OrderNumber = GenerateOrderNumber(),
            CustomerUserId = customerUserId,
            Items = orderItems,
            Subtotal = subtotal,
            CouponCode = cart.CouponCode,
            DiscountAmount = discount,
            DeliveryFee = request.DeliveryFee,
            Total = subtotal - discount + request.DeliveryFee,
            Status = OrderStatus.Processing,
            PaymentStatus = PaymentStatus.Pending,
            ShippingAddress = request.ShippingAddress,
            StatusHistory = [new OrderStatusEvent { Status = OrderStatus.Processing, Note = "Order placed." }]
        };

        await orders.AddAsync(order, ct);

        cart.Items.Clear();
        cart.CouponCode = null;
        cart.UpdatedAt = DateTime.UtcNow;
        await carts.UpdateAsync(cart, ct);

        return Map(order);
    }

    public async Task<List<OrderResponse>> GetForCustomerAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var list = await orders.FindAsync(o => o.BusinessId == businessId && o.CustomerUserId == customerUserId, ct);
        return list.OrderByDescending(o => o.PlacedAt).Select(Map).ToList();
    }

    public async Task<List<OrderResponse>> GetForBusinessAsync(string tenantId, string businessId, CancellationToken ct = default)
    {
        var list = await orders.FindAsync(o => o.TenantId == tenantId && o.BusinessId == businessId, ct);
        return list.OrderByDescending(o => o.PlacedAt).Select(Map).ToList();
    }

    public async Task<List<OrderResponse>> GetAssignedToAgentAsync(string businessId, string deliveryAgentUserId, CancellationToken ct = default)
    {
        var list = await orders.FindAsync(o => o.BusinessId == businessId && o.DeliveryAgentUserId == deliveryAgentUserId, ct);
        return list.OrderByDescending(o => o.PlacedAt).Select(Map).ToList();
    }

    public async Task<OrderResponse> GetByIdForCustomerAsync(string businessId, string customerUserId, string orderId, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BusinessId != businessId || order.CustomerUserId != customerUserId)
        {
            throw new NotFoundException(nameof(Order), orderId);
        }

        return Map(order);
    }

    public async Task<OrderResponse> GetByIdForBusinessAsync(string tenantId, string businessId, string orderId, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);
        return Map(order);
    }

    public async Task<OrderResponse> UpdateStatusAsync(string tenantId, string businessId, string orderId, UpdateOrderStatusRequest request, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);

        if (request.Status == OrderStatus.Cancelled && order.Status != OrderStatus.Cancelled)
        {
            await RestockAsync(order, ct);
        }

        order.Status = request.Status;
        order.StatusHistory.Add(new OrderStatusEvent { Status = request.Status, Note = request.Note });
        order.UpdatedAt = DateTime.UtcNow;
        await orders.UpdateAsync(order, ct);
        return Map(order);
    }

    public async Task<OrderResponse> AssignDeliveryAgentAsync(string tenantId, string businessId, string orderId, AssignDeliveryAgentRequest request, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (!business.DeliveryModuleEnabled)
        {
            throw new ConflictException("Delivery module is disabled for this business.");
        }

        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);

        order.DeliveryAgentUserId = request.DeliveryAgentUserId;
        if (order.Status is OrderStatus.Processing or OrderStatus.Confirmed)
        {
            order.Status = OrderStatus.OutForDelivery;
            order.StatusHistory.Add(new OrderStatusEvent { Status = OrderStatus.OutForDelivery, Note = "Delivery agent assigned." });
        }

        order.UpdatedAt = DateTime.UtcNow;
        await orders.UpdateAsync(order, ct);
        return Map(order);
    }

    public async Task<OrderResponse> CancelAsync(string businessId, string customerUserId, string orderId, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null || order.BusinessId != businessId || order.CustomerUserId != customerUserId)
        {
            throw new NotFoundException(nameof(Order), orderId);
        }

        if (!CancellableStatuses.Contains(order.Status))
        {
            throw new ConflictException($"An order in status '{order.Status}' can no longer be cancelled.");
        }

        await RestockAsync(order, ct);

        order.Status = OrderStatus.Cancelled;
        order.StatusHistory.Add(new OrderStatusEvent { Status = OrderStatus.Cancelled, Note = "Cancelled by customer." });
        order.UpdatedAt = DateTime.UtcNow;
        await orders.UpdateAsync(order, ct);
        return Map(order);
    }

    private async Task RestockAsync(Order order, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var product = await products.GetByIdAsync(item.ProductId, ct);
            if (product is null || !product.TrackInventory)
            {
                continue;
            }

            product.StockQuantity += item.Quantity;
            product.UpdatedAt = DateTime.UtcNow;
            await products.UpdateAsync(product, ct);
        }
    }

    private async Task<Order> GetScopedAsync(string tenantId, string businessId, string orderId, CancellationToken ct)
    {
        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null || order.TenantId != tenantId || order.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(Order), orderId);
        }

        return order;
    }

    private static string GenerateOrderNumber() =>
        $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static OrderResponse Map(Order o) => new(
        o.Id, o.BusinessId, o.OrderNumber, o.CustomerUserId,
        o.Items.Select(i => new OrderItemResponse(i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal)).ToList(),
        o.Subtotal, o.CouponCode, o.DiscountAmount, o.DeliveryFee, o.Total, o.Status, o.PaymentStatus,
        o.ShippingAddress, o.DeliveryAgentUserId, o.PlacedAt);
}

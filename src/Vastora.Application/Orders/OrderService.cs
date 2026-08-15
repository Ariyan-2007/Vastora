using Microsoft.Extensions.Logging;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Application.Inventory;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Orders;

public class OrderService(
    IMongoRepository<Order> orders,
    IMongoRepository<Domain.Entities.Cart> carts,
    IMongoRepository<Product> products,
    IMongoRepository<Business> businesses,
    IMongoRepository<DeliveryAgentProfile> deliveryAgentProfiles,
    IMongoRepository<AppUser> users,
    IMongoRepository<LedgerEntry> ledgerEntries,
    ICouponService couponService,
    INotificationService notificationService,
    IInventoryService inventoryService,
    ILogger<OrderService> logger) : IOrderService
{
    private static readonly HashSet<OrderStatus> CancellableStatuses = [OrderStatus.PendingPayment, OrderStatus.Processing, OrderStatus.Confirmed];

    /// <summary>
    /// Legal next statuses from each OrderStatus — §9.7. A caller-requested transition not
    /// listed here (and not a same-status no-op, allowed separately) 409s rather than silently
    /// applying. AssignDeliveryAgentAsync's own Processing/Confirmed → OutForDelivery jump is
    /// deliberately mirrored here too, so the two mechanisms never disagree about what's legal.
    /// </summary>
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.PendingPayment] = [OrderStatus.Processing, OrderStatus.Cancelled],
        [OrderStatus.Processing] = [OrderStatus.Confirmed, OrderStatus.OutForDelivery, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.OutForDelivery, OrderStatus.Cancelled],
        [OrderStatus.OutForDelivery] = [OrderStatus.Delivered, OrderStatus.Cancelled],
        [OrderStatus.Delivered] = [OrderStatus.Refunded],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Refunded] = []
    };

    public async Task<OrderResponse> CheckoutAsync(string tenantId, string businessId, string customerUserId, CheckoutRequest request, CancellationToken ct = default)
    {
        var cart = await carts.FindOneAsync(c => c.BusinessId == businessId && c.CustomerUserId == customerUserId, ct);
        if (cart is null || cart.Items.Count == 0)
        {
            throw new ConflictException("Your cart is empty.");
        }

        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        // Generated up front (not inside the Order initializer below) so it exists in time to
        // use as the StockMovement's ReferenceOrderId during the loop — §9.15a.
        var orderNumber = GenerateOrderNumber();

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
                await inventoryService.RecordMovementAsync(
                    tenantId, businessId, product.Id, StockMovementType.Sale, -cartItem.Quantity,
                    "Order checkout", orderNumber, customerUserId, ct);
            }
        }

        var subtotal = orderItems.Sum(i => i.LineTotal);
        var discount = 0m;
        if (!string.IsNullOrWhiteSpace(cart.CouponCode))
        {
            discount = await couponService.ValidateAndPriceAsync(businessId, cart.CouponCode, subtotal, ct);
            await couponService.RegisterUsageAsync(businessId, cart.CouponCode, ct);
        }

        var deliveryFee = request.DeliveryFee ?? business.DefaultDeliveryFee;

        var order = new Order
        {
            TenantId = tenantId,
            BusinessId = businessId,
            OrderNumber = orderNumber,
            CustomerUserId = customerUserId,
            Items = orderItems,
            Subtotal = subtotal,
            CouponCode = cart.CouponCode,
            DiscountAmount = discount,
            DeliveryFee = deliveryFee,
            Total = subtotal - discount + deliveryFee,
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

        await NotifyCustomerAsync(order, "Order confirmed",
            $"Your order {order.OrderNumber} has been placed. Total: {order.Total:0.00}.", ct);

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

        if (request.Status != order.Status && !AllowedTransitions[order.Status].Contains(request.Status))
        {
            throw new ConflictException($"Cannot move an order from '{order.Status}' to '{request.Status}'.");
        }

        if (request.Status == OrderStatus.Cancelled && order.Status != OrderStatus.Cancelled)
        {
            await RestockAsync(order, ct);
        }

        if (request.Status == OrderStatus.Delivered && order.Status != OrderStatus.Delivered)
        {
            await CreditDeliveryAgentAsync(order, ct);
            await RecordLedgerEntryAsync(order, LedgerEntryType.Revenue, order.Total, ct);
        }

        if (request.Status == OrderStatus.Refunded && order.Status != OrderStatus.Refunded)
        {
            // Reachable only from Delivered per AllowedTransitions above, so a matching Revenue
            // entry always exists to offset — §9.16a.
            await RecordLedgerEntryAsync(order, LedgerEntryType.Refund, order.Total, ct);
        }

        order.Status = request.Status;
        order.StatusHistory.Add(new OrderStatusEvent { Status = request.Status, Note = request.Note });
        order.UpdatedAt = DateTime.UtcNow;
        await orders.UpdateAsync(order, ct);

        await NotifyCustomerAsync(order, "Order status updated",
            $"Your order {order.OrderNumber} is now '{order.Status}'.", ct);

        return Map(order);
    }

    public async Task<OrderResponse> UpdatePaymentStatusAsync(string tenantId, string businessId, string orderId, UpdatePaymentStatusRequest request, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);

        order.PaymentStatus = request.Status;
        order.PaymentStatusHistory.Add(new PaymentStatusEvent { Status = request.Status, Note = request.Note ?? string.Empty });
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

            await inventoryService.RecordMovementAsync(
                order.TenantId, order.BusinessId, item.ProductId, StockMovementType.Return, item.Quantity,
                "Order cancelled", order.OrderNumber, null, ct);
        }
    }

    /// <summary>Pays the assigned agent their flat DeliveryCharge and logs the completion — §9.7. No-op if no agent was assigned (e.g. delivery module was off).</summary>
    private async Task CreditDeliveryAgentAsync(Order order, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(order.DeliveryAgentUserId))
        {
            return;
        }

        var profile = await deliveryAgentProfiles.FindOneAsync(
            p => p.BusinessId == order.BusinessId && p.UserId == order.DeliveryAgentUserId, ct);
        if (profile is null)
        {
            return;
        }

        profile.Balance += profile.DeliveryCharge;
        profile.CompletedDeliveries += 1;
        profile.UpdatedAt = DateTime.UtcNow;
        await deliveryAgentProfiles.UpdateAsync(profile, ct);

        await RecordLedgerEntryAsync(order, LedgerEntryType.DeliveryPayout, profile.DeliveryCharge, ct);
    }

    /// <summary>Written by OrderService only — no controller can create a LedgerEntry directly (§9.16a).</summary>
    private async Task RecordLedgerEntryAsync(Order order, LedgerEntryType type, decimal amount, CancellationToken ct)
    {
        var business = await businesses.GetByIdAsync(order.BusinessId, ct);
        await ledgerEntries.AddAsync(new LedgerEntry
        {
            TenantId = order.TenantId,
            BusinessId = order.BusinessId,
            Type = type,
            Amount = amount,
            Currency = business?.Currency ?? string.Empty,
            ReferenceOrderId = order.OrderNumber
        }, ct);
    }

    /// <summary>
    /// Best-effort — a notification failure must never fail the order operation that triggered
    /// it (§9.10). Today's INotificationService just logs, so this mostly guards against a
    /// future real provider's transient errors once one is wired in.
    /// </summary>
    private async Task NotifyCustomerAsync(Order order, string subject, string body, CancellationToken ct)
    {
        try
        {
            var customer = await users.GetByIdAsync(order.CustomerUserId, ct);
            if (customer is not null)
            {
                await notificationService.NotifyAsync(new NotificationMessage(customer.Email, subject, body), ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify customer for order {OrderId}", order.Id);
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
        o.ShippingAddress, o.DeliveryAgentUserId,
        o.StatusHistory.Select(e => new OrderStatusEventResponse(e.Status, e.Timestamp, e.Note)).ToList(),
        o.PaymentStatusHistory.Select(e => new PaymentStatusEventResponse(e.Status, e.Timestamp, e.Note)).ToList(),
        o.PlacedAt);
}

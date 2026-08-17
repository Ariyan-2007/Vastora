using Microsoft.Extensions.Logging;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Coupons;
using Vastora.Application.GiftCards;
using Vastora.Application.Inventory;
using Vastora.Application.Notifications;
using Vastora.Application.Pricing;
using Vastora.Application.Promotions;
using Vastora.Application.Webhooks;
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
    IPricingService pricingService,
    IPromotionService promotionService,
    IGiftCardService giftCardService,
    IStoreCreditService storeCreditService,
    INotificationService notificationService,
    IInventoryService inventoryService,
    IWebhookPublisher webhookPublisher,
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

    // ---------------------------------------------------------------------------------------
    // Checkout — §9.17
    // ---------------------------------------------------------------------------------------

    public async Task<OrderResponse> CheckoutAsync(
        string tenantId, string businessId, string? customerUserId, string? guestToken,
        CheckoutRequest request, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        var isGuest = string.IsNullOrEmpty(customerUserId);
        if (isGuest)
        {
            if (!business.GuestCheckoutEnabled)
            {
                throw new ConflictException("This shop requires an account to place an order.");
            }

            if (string.IsNullOrWhiteSpace(request.GuestEmail))
            {
                throw new ConflictException("An email address is required to place a guest order.");
            }
        }

        var cart = await LoadCartAsync(businessId, customerUserId, guestToken, ct);

        // Phase 1 — resolve and validate every line before anything is written. The old
        // implementation deducted stock inside this loop, so a failure on line 3 left lines 1
        // and 2 permanently deducted with no order to account for them (§9.17).
        var lines = await pricingService.ResolveLinesAsync(businessId, cart.Items, customerUserId, ct);
        ValidatePurchasable(lines);

        var breakdown = await pricingService.PriceAsync(
            BuildPricingContext(business, customerUserId, cart, request), lines, ct);

        // Generated before the order is inserted so it can reference the stock movements written
        // in phase 2, which happen first — §9.15a.
        var orderNumber = GenerateOrderNumber();

        // Phase 2 — take the stock. Each deduction is atomic and refuses to go below zero, so
        // concurrent checkouts for the last unit cannot both succeed. If any line fails, every
        // deduction already made in this checkout is put back before the error surfaces.
        var consumed = await ConsumeStockAsync(tenantId, businessId, lines, orderNumber, customerUserId, ct);

        try
        {
            var order = BuildOrder(tenantId, business, customerUserId, orderNumber, request, breakdown, isGuest);
            await orders.AddAsync(order, ct);

            await SettleAsync(tenantId, business, order, breakdown, customerUserId, ct);
            await ClearCartAsync(cart, ct);

            await NotifyCustomerAsync(order, b => EmailTemplates.OrderConfirmation(b, order), ct);

            await webhookPublisher.PublishAsync(tenantId, businessId, WebhookEvents.OrderCreated, MapForWebhook(order), ct);

            return Map(order);
        }
        catch
        {
            // The order never came into existence, so the stock taken for it must go back.
            await CompensateStockAsync(tenantId, businessId, consumed, orderNumber, ct);
            throw;
        }
    }

    public async Task<CheckoutPreviewResponse> PreviewAsync(
        string businessId, string? customerUserId, string? guestToken,
        CheckoutRequest request, CancellationToken ct = default)
    {
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        var cart = await LoadCartAsync(businessId, customerUserId, guestToken, ct);
        var lines = await pricingService.ResolveLinesAsync(businessId, cart.Items, customerUserId, ct);
        var breakdown = await pricingService.PriceAsync(
            BuildPricingContext(business, customerUserId, cart, request), lines, ct);

        var creditAvailable = string.IsNullOrEmpty(customerUserId)
            ? 0m
            : await storeCreditService.GetBalanceAsync(businessId, customerUserId, ct);

        return new CheckoutPreviewResponse(
            breakdown.Subtotal,
            [.. breakdown.Discounts.Select(d => new OrderDiscountResponse(d.Source, d.Label, d.Amount))],
            breakdown.DiscountTotal,
            breakdown.DeliveryFee,
            breakdown.ShippingMethodName,
            [.. breakdown.ShippingOptions],
            breakdown.Tax.TotalTax,
            [.. breakdown.Tax.Lines],
            breakdown.Tax.PricesIncludeTax,
            breakdown.Total,
            breakdown.GiftCardTotal,
            creditAvailable,
            breakdown.AmountDue,
            breakdown.Currency);
    }

    private static PricingContext BuildPricingContext(
        Business business, string? customerUserId, Domain.Entities.Cart cart, CheckoutRequest request) =>
        new(business,
            string.IsNullOrEmpty(customerUserId) ? null : customerUserId,
            request.ShippingAddress,
            cart.CouponCode,
            cart.PromotionCodes,
            [.. (request.GiftCardCodes ?? []).Concat(cart.GiftCardCodes).Distinct(StringComparer.OrdinalIgnoreCase)],
            request.ShippingRateId,
            request.DeliveryFee,
            request.UseStoreCredit);

    private async Task<Domain.Entities.Cart> LoadCartAsync(string businessId, string? customerUserId, string? guestToken, CancellationToken ct)
    {
        var cart = string.IsNullOrEmpty(customerUserId)
            ? await carts.FindOneAsync(c => c.BusinessId == businessId && c.GuestToken == guestToken, ct)
            : await carts.FindOneAsync(c => c.BusinessId == businessId && c.CustomerUserId == customerUserId, ct);

        if (cart is null || cart.Items.Count == 0)
        {
            throw new ConflictException("Your cart is empty.");
        }

        return cart;
    }

    /// <summary>
    /// Everything that makes a line un-buyable, checked up front so no stock has moved by the
    /// time we find out. Availability itself is *not* checked here — a read-then-write check
    /// cannot be trusted under concurrency, so that guarantee lives in the atomic deduction.
    /// </summary>
    private static void ValidatePurchasable(IReadOnlyList<ResolvedLine> lines)
    {
        var now = DateTime.UtcNow;

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ConflictException($"'{line.ProductName}' has an invalid quantity.");
            }

            if (!line.Product.IsPubliclyVisibleNow(now))
            {
                throw new ConflictException($"'{line.ProductName}' is no longer available.");
            }
        }
    }

    private record ConsumedLine(string ProductId, string? VariantId, int Quantity);

    private async Task<List<ConsumedLine>> ConsumeStockAsync(
        string tenantId, string businessId, IReadOnlyList<ResolvedLine> lines,
        string orderNumber, string? customerUserId, CancellationToken ct)
    {
        var consumed = new List<ConsumedLine>();

        foreach (var line in lines)
        {
            if (!line.Product.TrackInventory)
            {
                continue;
            }

            var ok = await inventoryService.TryConsumeStockAsync(
                tenantId, businessId, line.ProductId, line.VariantId, line.Quantity,
                "Order checkout", orderNumber, customerUserId, ct);

            if (ok)
            {
                consumed.Add(new ConsumedLine(line.ProductId, line.VariantId, line.Quantity));
                continue;
            }

            // Put back everything this checkout already took, then report the line that failed.
            await CompensateStockAsync(tenantId, businessId, consumed, orderNumber, ct);

            var available = line.Variant?.StockQuantity ?? line.Product.StockQuantity;
            throw new ConflictException($"'{line.ProductName}' only has {Math.Max(0, available)} left in stock.");
        }

        return consumed;
    }

    /// <summary>
    /// Compensating rollback (§9.17). MongoDB multi-document transactions would be stronger, but
    /// they require threading an IClientSessionHandle through every repository call; compensation
    /// gets the same observable outcome for the failure modes that actually occur here, and is
    /// honest about being best-effort — a compensation that itself fails is logged loudly rather
    /// than swallowed, because that is the one case leaving stock stranded.
    /// </summary>
    private async Task CompensateStockAsync(
        string tenantId, string businessId, List<ConsumedLine> consumed, string orderNumber, CancellationToken ct)
    {
        foreach (var line in consumed)
        {
            try
            {
                await inventoryService.RecordMovementAsync(
                    tenantId, businessId, line.ProductId, StockMovementType.Return, line.Quantity,
                    "Checkout rolled back", orderNumber, null, line.VariantId, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to restore {Quantity} of product {ProductId} after checkout {OrderNumber} was rolled back. Stock is now understated.",
                    line.Quantity, line.ProductId, orderNumber);
            }
        }
    }

    private static Order BuildOrder(
        string tenantId, Business business, string? customerUserId, string orderNumber,
        CheckoutRequest request, PriceBreakdown breakdown, bool isGuest) =>
        new()
        {
            TenantId = tenantId,
            BusinessId = business.Id,
            OrderNumber = orderNumber,
            CustomerUserId = customerUserId ?? string.Empty,
            IsGuestOrder = isGuest,
            ContactEmail = request.GuestEmail ?? string.Empty,
            ContactPhone = request.GuestPhone ?? request.ShippingAddress?.Phone ?? string.Empty,
            GuestAccessToken = isGuest ? Guid.NewGuid().ToString("N") : null,
            Items = [.. breakdown.Lines.Select(l => new OrderItem
            {
                ProductId = l.ProductId,
                VariantId = l.VariantId,
                VariantSummary = l.VariantSummary,
                ProductName = l.ProductName,
                UnitPrice = l.UnitPrice,
                UnitCost = l.UnitCost,
                Quantity = l.Quantity
            })],
            Subtotal = breakdown.Subtotal,
            CouponCode = breakdown.Discounts.FirstOrDefault(d => d.Source == "Coupon")?.Label,
            DiscountAmount = breakdown.DiscountTotal,
            Discounts = [.. breakdown.Discounts.Select(d => new OrderDiscountLine
            {
                Source = d.Source,
                Label = d.Label,
                Amount = d.Amount
            })],
            AppliedPromotionIds = [.. breakdown.AppliedPromotionIds],
            DeliveryFee = breakdown.DeliveryFee,
            ShippingMethodName = breakdown.ShippingMethodName,
            TaxAmount = breakdown.Tax.TotalTax,
            TaxRatePercent = breakdown.Tax.EffectiveRatePercent,
            PricesIncludeTax = breakdown.Tax.PricesIncludeTax,
            Total = breakdown.Total,
            Currency = breakdown.Currency,
            GiftCardsUsed = [.. breakdown.GiftCardUses],
            StoreCreditApplied = breakdown.StoreCreditApplied,
            AmountDue = breakdown.AmountDue,
            Status = OrderStatus.Processing,
            PaymentStatus = PaymentStatus.Pending,
            FulfillmentMethod = request.FulfillmentMethod,
            ShippingAddress = request.ShippingAddress,
            BillingAddress = request.BillingAddress ?? request.ShippingAddress,
            CustomerNote = request.CustomerNote ?? string.Empty,
            StatusHistory = [new OrderStatusEvent { Status = OrderStatus.Processing, Note = "Order placed." }]
        };

    /// <summary>
    /// Burns the redemptions the order actually consumed. Deliberately after the order is
    /// persisted: a usage recorded against an order that failed to save would be unrecoverable,
    /// whereas an order that saved and then failed to burn a coupon is merely generous.
    /// </summary>
    private async Task SettleAsync(
        string tenantId, Business business, Order order, PriceBreakdown breakdown,
        string? customerUserId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(order.CouponCode))
        {
            await couponService.RegisterUsageAsync(business.Id, order.CouponCode, ct);
        }

        if (breakdown.AppliedPromotionIds.Count > 0)
        {
            await promotionService.RegisterUsageAsync(breakdown.AppliedPromotionIds, ct);
        }

        if (breakdown.GiftCardUses.Count > 0)
        {
            await giftCardService.RedeemAsync(breakdown.GiftCardUses, ct);
        }

        if (breakdown.StoreCreditApplied > 0 && !string.IsNullOrEmpty(customerUserId))
        {
            await storeCreditService.SpendAsync(
                tenantId, business.Id, customerUserId, breakdown.StoreCreditApplied, order.OrderNumber, ct);
        }
    }

    private async Task ClearCartAsync(Domain.Entities.Cart cart, CancellationToken ct)
    {
        cart.Items.Clear();
        cart.CouponCode = null;
        cart.PromotionCodes.Clear();
        cart.GiftCardCodes.Clear();
        cart.AbandonedReminderSentAt = null;
        await carts.UpdateAsync(cart, ct);
    }

    // ---------------------------------------------------------------------------------------
    // Reads — §9.18
    // ---------------------------------------------------------------------------------------

    public async Task<PagedResult<OrderResponse>> GetForCustomerAsync(string businessId, string customerUserId, PageRequest page, CancellationToken ct = default)
    {
        var result = await orders.FindPagedAsync(
            o => o.BusinessId == businessId && o.CustomerUserId == customerUserId, page, o => o.PlacedAt, ct: ct);
        return result.Map(Map);
    }

    public async Task<PagedResult<OrderResponse>> GetForBusinessAsync(string tenantId, string businessId, OrderQuery query, CancellationToken ct = default)
    {
        var page = PageRequest.Of(query.Page, query.PageSize);
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        // Every filter is in the predicate, so MongoDB evaluates them — the old implementation
        // materialised every order for the business and sorted the lot in memory (§9.18).
        var result = await orders.FindPagedAsync(
            o => o.TenantId == tenantId
                 && o.BusinessId == businessId
                 && (query.Status == null || o.Status == query.Status)
                 && (query.PaymentStatus == null || o.PaymentStatus == query.PaymentStatus)
                 && (query.From == null || o.PlacedAt >= query.From)
                 && (query.To == null || o.PlacedAt <= query.To)
                 && (search == null || o.OrderNumber.Contains(search) || o.ContactEmail.Contains(search)),
            page, o => o.PlacedAt, ct: ct);

        return result.Map(Map);
    }

    public async Task<PagedResult<OrderResponse>> GetAssignedToAgentAsync(string businessId, string deliveryAgentUserId, PageRequest page, CancellationToken ct = default)
    {
        var result = await orders.FindPagedAsync(
            o => o.BusinessId == businessId && o.DeliveryAgentUserId == deliveryAgentUserId, page, o => o.PlacedAt, ct: ct);
        return result.Map(Map);
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

    public async Task<OrderResponse> LookupGuestOrderAsync(string businessId, string orderNumber, string email, CancellationToken ct = default)
    {
        var normalised = email.Trim().ToLowerInvariant();
        var order = await orders.FindOneAsync(
            o => o.BusinessId == businessId && o.OrderNumber == orderNumber, ct);

        // Email is compared after the fetch rather than in the predicate so the comparison can be
        // case-insensitive without a regex scan; a mismatch 404s exactly like a wrong order number,
        // giving nothing away about which half was wrong.
        if (order is null || !string.Equals(order.ContactEmail, normalised, StringComparison.OrdinalIgnoreCase))
        {
            throw new NotFoundException(nameof(Order), orderNumber);
        }

        return Map(order);
    }

    public async Task<OrderResponse> GetByIdForBusinessAsync(string tenantId, string businessId, string orderId, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);
        return Map(order);
    }

    // ---------------------------------------------------------------------------------------
    // Transitions
    // ---------------------------------------------------------------------------------------

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
            await RefundSettlementsAsync(order, ct);
        }

        if (request.Status == OrderStatus.Delivered && order.Status != OrderStatus.Delivered)
        {
            await CreditDeliveryAgentAsync(order, ct);
            await RecordRevenueAsync(order, ct);
        }

        if (request.Status == OrderStatus.Refunded && order.Status != OrderStatus.Refunded)
        {
            // Reachable only from Delivered per AllowedTransitions above, so a matching Revenue
            // entry always exists to offset — §9.16a. Unlike before, this now also puts the goods
            // back into stock: a refunded item used to vanish from inventory entirely (§9.21).
            await RestockAsync(order, ct);
            await RecordLedgerEntryAsync(order, LedgerEntryType.Refund, order.Total - order.RefundedAmount, ct);
            await RefundSettlementsAsync(order, ct);

            order.RefundedAmount = order.Total;
            foreach (var item in order.Items)
            {
                item.RefundedQuantity = item.Quantity;
            }
        }

        order.Status = request.Status;
        order.StatusHistory.Add(new OrderStatusEvent { Status = request.Status, Note = request.Note });
        await orders.UpdateAsync(order, ct);

        await NotifyCustomerAsync(order, b => EmailTemplates.OrderStatusUpdate(b, order, request.Note), ct);

        await webhookPublisher.PublishAsync(tenantId, businessId, WebhookEvents.OrderStatusChanged, MapForWebhook(order), ct);

        if (order.Status == OrderStatus.Delivered)
        {
            await webhookPublisher.PublishAsync(tenantId, businessId, WebhookEvents.OrderDelivered, MapForWebhook(order), ct);
        }

        return Map(order);
    }

    public async Task<OrderResponse> UpdatePaymentStatusAsync(string tenantId, string businessId, string orderId, UpdatePaymentStatusRequest request, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);

        order.PaymentStatus = request.Status;
        order.PaymentStatusHistory.Add(new PaymentStatusEvent { Status = request.Status, Note = request.Note ?? string.Empty });
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

        await orders.UpdateAsync(order, ct);
        return Map(order);
    }

    public async Task<OrderResponse> UpdateShipmentAsync(string tenantId, string businessId, string orderId, UpdateShipmentRequest request, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);

        order.CarrierName = request.CarrierName;
        order.TrackingNumber = request.TrackingNumber;
        order.TrackingUrl = request.TrackingUrl;
        if (!string.IsNullOrWhiteSpace(request.ShippingMethodName))
        {
            order.ShippingMethodName = request.ShippingMethodName;
        }

        // Recording a tracking number *is* dispatching the order, so move it along rather than
        // making staff perform a second status change that can be forgotten.
        if (!string.IsNullOrWhiteSpace(request.TrackingNumber)
            && order.Status is OrderStatus.Processing or OrderStatus.Confirmed)
        {
            order.Status = OrderStatus.OutForDelivery;
            order.StatusHistory.Add(new OrderStatusEvent
            {
                Status = OrderStatus.OutForDelivery,
                Note = $"Shipped via {request.CarrierName ?? "courier"} ({request.TrackingNumber})."
            });
        }

        await orders.UpdateAsync(order, ct);

        await NotifyCustomerAsync(order, b => EmailTemplates.OrderShipped(b, order, request.CarrierName, request.TrackingNumber, order.TrackingUrl), ct);

        return Map(order);
    }

    public async Task<OrderResponse> UpdateInternalNoteAsync(string tenantId, string businessId, string orderId, UpdateOrderNoteRequest request, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);
        order.InternalNote = request.InternalNote;
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
        await RefundSettlementsAsync(order, ct);

        order.Status = OrderStatus.Cancelled;
        order.StatusHistory.Add(new OrderStatusEvent { Status = OrderStatus.Cancelled, Note = "Cancelled by customer." });
        await orders.UpdateAsync(order, ct);
        return Map(order);
    }

    // ---------------------------------------------------------------------------------------
    // Invoicing — §9.33
    // ---------------------------------------------------------------------------------------

    public async Task<InvoiceResponse> GetInvoiceAsync(string tenantId, string businessId, string orderId, CancellationToken ct = default)
    {
        var order = await GetScopedAsync(tenantId, businessId, orderId, ct);
        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        if (string.IsNullOrEmpty(order.InvoiceNumber))
        {
            // Atomic increment on the Business document, so two staff opening the same order at
            // once cannot be handed the same invoice number — which for a legally sequential
            // document is not a cosmetic problem.
            var next = await businesses.NextSequenceAsync(businessId, b => b.Invoicing.LastNumber, ct)
                ?? throw new NotFoundException(nameof(Business), businessId);

            order.InvoiceNumber = $"{business.Invoicing.NumberPrefix}{next:D6}";
            order.InvoicedAt = DateTime.UtcNow;
            await orders.UpdateAsync(order, ct);
        }

        var buyer = string.IsNullOrEmpty(order.CustomerUserId)
            ? null
            : await users.GetByIdAsync(order.CustomerUserId, ct);

        var amountPaid = order.PaymentStatus == PaymentStatus.Paid
            ? order.Total - order.RefundedAmount
            : order.GiftCardsUsed.Sum(g => g.AmountApplied) + order.StoreCreditApplied;

        return new InvoiceResponse(
            order.InvoiceNumber!,
            order.InvoicedAt ?? DateTime.UtcNow,
            order.OrderNumber,
            order.PlacedAt,
            string.IsNullOrWhiteSpace(business.Invoicing.LegalName) ? business.Name : business.Invoicing.LegalName,
            business.Invoicing.LegalAddress,
            business.Invoicing.RegistrationNumber,
            business.Tax.RegistrationNumber,
            buyer?.FullName ?? order.ShippingAddress?.Label ?? "Guest",
            buyer?.Email ?? order.ContactEmail,
            order.BillingAddress,
            order.ShippingAddress,
            [.. order.Items.Select(MapItem)],
            order.Subtotal,
            [.. order.Discounts.Select(d => new OrderDiscountResponse(d.Source, d.Label, d.Amount))],
            order.DiscountAmount,
            order.DeliveryFee,
            business.Tax.DisplayName,
            order.TaxRatePercent,
            order.TaxAmount,
            order.PricesIncludeTax,
            order.Total,
            Math.Max(0m, amountPaid),
            Math.Max(0m, order.Total - amountPaid),
            order.Currency,
            business.Invoicing.FooterNote);
    }

    // ---------------------------------------------------------------------------------------
    // Internals
    // ---------------------------------------------------------------------------------------

    private async Task RestockAsync(Order order, CancellationToken ct)
    {
        foreach (var item in order.Items)
        {
            var outstanding = item.Quantity - item.RefundedQuantity;
            if (outstanding <= 0)
            {
                continue;
            }

            var product = await products.GetByIdAsync(item.ProductId, ct);
            if (product is null || !product.TrackInventory)
            {
                continue;
            }

            await inventoryService.RecordMovementAsync(
                order.TenantId, order.BusinessId, item.ProductId, StockMovementType.Return, outstanding,
                $"Order {order.Status}", order.OrderNumber, null, item.VariantId, ct);
        }
    }

    /// <summary>Gift cards and store credit spent on a cancelled or refunded order go back to the customer (§9.24).</summary>
    private async Task RefundSettlementsAsync(Order order, CancellationToken ct)
    {
        if (order.GiftCardsUsed.Count > 0)
        {
            await giftCardService.RefundAsync(order.GiftCardsUsed, ct);
        }

        if (order.StoreCreditApplied > 0 && !string.IsNullOrEmpty(order.CustomerUserId))
        {
            await storeCreditService.RecordAsync(
                order.TenantId, order.BusinessId, order.CustomerUserId, order.StoreCreditApplied,
                StoreCreditReason.RefundToCredit, $"Order {order.OrderNumber} reversed", order.OrderNumber, ct: ct);
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
        await deliveryAgentProfiles.UpdateAsync(profile, ct);

        await RecordLedgerEntryAsync(order, LedgerEntryType.DeliveryPayout, profile.DeliveryCharge, ct);
    }

    /// <summary>
    /// §9.31. Delivery books three lines, not one: gross Revenue, the CostOfGoodsSold behind it,
    /// and any TaxCollected. Splitting them is what makes gross margin computable and keeps tax —
    /// which is a liability owed onward, not earnings — out of the profit figure.
    /// </summary>
    private async Task RecordRevenueAsync(Order order, CancellationToken ct)
    {
        await RecordLedgerEntryAsync(order, LedgerEntryType.Revenue, order.Total - order.TaxAmount, ct);

        var cogs = order.Items.Sum(i => i.LineCost ?? 0m);
        if (cogs > 0)
        {
            await RecordLedgerEntryAsync(order, LedgerEntryType.CostOfGoodsSold, cogs, ct);
        }

        if (order.TaxAmount > 0)
        {
            await RecordLedgerEntryAsync(order, LedgerEntryType.TaxCollected, order.TaxAmount, ct);
        }
    }

    /// <summary>Written by OrderService only — no controller can create a LedgerEntry directly (§9.16a).</summary>
    private async Task RecordLedgerEntryAsync(Order order, LedgerEntryType type, decimal amount, CancellationToken ct)
    {
        if (amount <= 0)
        {
            return;
        }

        await ledgerEntries.AddAsync(new LedgerEntry
        {
            TenantId = order.TenantId,
            BusinessId = order.BusinessId,
            Type = type,
            Amount = amount,
            // Snapshotted onto the order at checkout (§9.38), so a later Business currency change
            // can't retroactively reinterpret this entry.
            Currency = order.Currency,
            ReferenceOrderId = order.OrderNumber
        }, ct);
    }

    /// <summary>
    /// Best-effort — a notification failure must never fail the order operation that triggered
    /// it (§9.10). Also resolves a guest order's contact email, which has no AppUser behind it,
    /// and the order's own Business so the email template can carry its logo/brand color.
    /// </summary>
    private async Task NotifyCustomerAsync(Order order, Func<Business?, (string Subject, string PlainBody, string HtmlBody)> buildMessage, CancellationToken ct)
    {
        try
        {
            var email = order.ContactEmail;

            if (string.IsNullOrWhiteSpace(email) && !string.IsNullOrEmpty(order.CustomerUserId))
            {
                var customer = await users.GetByIdAsync(order.CustomerUserId, ct);
                if (customer is null || !customer.NotificationPreferences.TransactionalEmail)
                {
                    return;
                }

                email = customer.Email;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                var business = await businesses.GetByIdAsync(order.BusinessId, ct);
                var (subject, plainBody, htmlBody) = buildMessage(business);
                await notificationService.NotifyAsync(new NotificationMessage(email, subject, plainBody, htmlBody), ct);
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

    private static object MapForWebhook(Order o) => new
    {
        orderId = o.Id,
        orderNumber = o.OrderNumber,
        businessId = o.BusinessId,
        status = o.Status.ToString(),
        paymentStatus = o.PaymentStatus.ToString(),
        total = o.Total,
        currency = o.Currency,
        placedAt = o.PlacedAt
    };

    private static OrderItemResponse MapItem(OrderItem i) => new(
        i.ProductId, i.VariantId, i.VariantSummary, i.ProductName, i.UnitPrice, i.Quantity, i.RefundedQuantity, i.LineTotal);

    private static OrderResponse Map(Order o) => new(
        o.Id, o.BusinessId, o.OrderNumber, o.CustomerUserId, o.IsGuestOrder, o.ContactEmail, o.ContactPhone,
        [.. o.Items.Select(MapItem)],
        o.Subtotal, o.CouponCode, o.DiscountAmount,
        [.. o.Discounts.Select(d => new OrderDiscountResponse(d.Source, d.Label, d.Amount))],
        o.DeliveryFee, o.TaxAmount, o.TaxRatePercent, o.PricesIncludeTax, o.Total,
        o.GiftCardsUsed.Sum(g => g.AmountApplied),
        [.. o.GiftCardsUsed.Select(g => new OrderGiftCardResponse(g.CodeSuffix, g.AmountApplied))],
        o.StoreCreditApplied, o.AmountDue, o.RefundedAmount, o.Currency,
        o.Status, o.PaymentStatus, o.FulfillmentMethod,
        o.ShippingAddress, o.BillingAddress, o.DeliveryAgentUserId,
        o.ShippingMethodName, o.CarrierName, o.TrackingNumber, o.TrackingUrl, o.InvoiceNumber, o.CustomerNote,
        [.. o.StatusHistory.Select(e => new OrderStatusEventResponse(e.Status, e.Timestamp, e.Note))],
        [.. o.PaymentStatusHistory.Select(e => new PaymentStatusEventResponse(e.Status, e.Timestamp, e.Note))],
        o.PlacedAt);
}

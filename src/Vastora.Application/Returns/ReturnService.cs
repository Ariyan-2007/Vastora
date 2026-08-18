using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.GiftCards;
using Vastora.Application.Inventory;
using Vastora.Application.Notifications;
using Vastora.Application.Webhooks;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Returns;

public record ReturnLineRequest(string ProductId, string? VariantId, int Quantity);

public record CreateReturnRequest(
    string OrderId,
    List<ReturnLineRequest> Items,
    ReturnReason Reason,
    string ReasonNote,
    ReturnResolution Resolution);

/// <summary>Staff decision. <paramref name="ApprovedRefundAmount"/> null means "the full requested amount".</summary>
public record DecideReturnRequest(bool Approve, decimal? ApprovedRefundAmount, string Note);

public record ReturnItemResponse(string ProductId, string? VariantId, string ProductName, int Quantity, decimal UnitPrice, decimal LineRefund);

public record ReturnStatusEventResponse(ReturnStatus Status, DateTime Timestamp, string Note);

public record ReturnResponse(
    string Id,
    string RmaNumber,
    string OrderId,
    string OrderNumber,
    string CustomerUserId,
    List<ReturnItemResponse> Items,
    ReturnReason Reason,
    string ReasonNote,
    ReturnResolution Resolution,
    ReturnStatus Status,
    decimal RequestedRefundAmount,
    decimal? ApprovedRefundAmount,
    string Currency,
    bool Restocked,
    DateTime? RefundedAt,
    List<ReturnStatusEventResponse> StatusHistory,
    DateTime CreatedAt);

/// <summary>
/// §9.21. The workflow that previously did not exist: <c>OrderStatus.Refunded</c> was the whole
/// returns story — whole-order, all-or-nothing, customer-initiated nowhere, and (the actual bug)
/// it never put the goods back into stock.
/// </summary>
public interface IReturnService
{
    Task<ReturnResponse> RequestAsync(string tenantId, string businessId, string customerUserId, CreateReturnRequest request, CancellationToken ct = default);

    Task<PagedResult<ReturnResponse>> GetForCustomerAsync(string businessId, string customerUserId, PageRequest page, CancellationToken ct = default);

    Task<PagedResult<ReturnResponse>> GetForBusinessAsync(string businessId, ReturnStatus? status, PageRequest page, CancellationToken ct = default);

    Task<ReturnResponse> GetByIdAsync(string tenantId, string businessId, string returnId, CancellationToken ct = default);

    Task<ReturnResponse> DecideAsync(string tenantId, string businessId, string returnId, DecideReturnRequest request, string staffUserId, CancellationToken ct = default);

    /// <summary>Goods physically back with the seller. This — not approval — is what restocks.</summary>
    Task<ReturnResponse> MarkReceivedAsync(string tenantId, string businessId, string returnId, string staffUserId, CancellationToken ct = default);

    /// <summary>Settles the money and writes the partial Refund ledger entry.</summary>
    Task<ReturnResponse> RefundAsync(string tenantId, string businessId, string returnId, string staffUserId, CancellationToken ct = default);

    Task<ReturnResponse> CancelAsync(string businessId, string customerUserId, string returnId, CancellationToken ct = default);
}

/// <inheritdoc cref="IReturnService"/>
public class ReturnService(
    IMongoRepository<ReturnRequest> returns,
    IMongoRepository<Order> orders,
    IMongoRepository<Business> businesses,
    IMongoRepository<LedgerEntry> ledgerEntries,
    IInventoryService inventoryService,
    IStoreCreditService storeCreditService,
    IGiftCardService giftCardService,
    INotificationService notificationService,
    IWebhookPublisher webhookPublisher) : IReturnService
{
    public async Task<ReturnResponse> RequestAsync(string tenantId, string businessId, string customerUserId, CreateReturnRequest request, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(request.OrderId, ct);
        if (order is null || order.BusinessId != businessId || order.CustomerUserId != customerUserId)
        {
            throw new NotFoundException(nameof(Order), request.OrderId);
        }

        if (order.Status != OrderStatus.Delivered)
        {
            throw new ConflictException("Only delivered orders can be returned.");
        }

        var business = await businesses.GetByIdAsync(businessId, ct)
            ?? throw new NotFoundException(nameof(Business), businessId);

        if (business.ReturnWindowDays <= 0)
        {
            throw new ConflictException("This shop does not accept returns.");
        }

        var deliveredAt = order.StatusHistory
            .Where(e => e.Status == OrderStatus.Delivered)
            .Select(e => e.Timestamp)
            .DefaultIfEmpty(order.PlacedAt)
            .Max();

        if (DateTime.UtcNow > deliveredAt.AddDays(business.ReturnWindowDays))
        {
            throw new ConflictException($"The {business.ReturnWindowDays}-day return window for this order has closed.");
        }

        var items = BuildReturnItems(order, request.Items);

        var entity = new ReturnRequest
        {
            TenantId = tenantId,
            BusinessId = businessId,
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            RmaNumber = GenerateRmaNumber(),
            CustomerUserId = customerUserId,
            Items = items,
            Reason = request.Reason,
            ReasonNote = request.ReasonNote,
            Resolution = request.Resolution,
            // The delivery fee is deliberately not refunded automatically — shipping was performed
            // and, for a change-of-mind return, most sellers don't refund it. Staff can still
            // settle for a different amount when they approve.
            RequestedRefundAmount = items.Sum(i => i.LineRefund),
            Currency = order.Currency,
            StatusHistory = [new ReturnStatusEvent { Status = ReturnStatus.Requested, Note = "Return requested by customer." }]
        };

        await returns.AddAsync(entity, ct);

        await webhookPublisher.PublishAsync(tenantId, businessId, WebhookEvents.ReturnRequested,
            new { returnId = entity.Id, rmaNumber = entity.RmaNumber, orderNumber = order.OrderNumber }, ct);

        return Map(entity);
    }

    /// <summary>
    /// Validates each requested line against what the order actually contains and what has not
    /// already been returned, and prices it from the order's own snapshot — so a refund can never
    /// exceed what the customer paid, whatever the product costs today.
    /// </summary>
    private static List<ReturnItem> BuildReturnItems(Order order, List<ReturnLineRequest> requested)
    {
        if (requested.Count == 0)
        {
            throw new ConflictException("A return must include at least one item.");
        }

        var items = new List<ReturnItem>();

        foreach (var line in requested)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.ProductId == line.ProductId && i.VariantId == line.VariantId)
                ?? throw new ConflictException($"Order {order.OrderNumber} doesn't contain that item.");

            var returnable = orderItem.Quantity - orderItem.RefundedQuantity;
            if (line.Quantity <= 0 || line.Quantity > returnable)
            {
                throw new ConflictException(
                    $"'{orderItem.ProductName}': you can return at most {returnable} of this item.");
            }

            items.Add(new ReturnItem
            {
                ProductId = orderItem.ProductId,
                VariantId = orderItem.VariantId,
                ProductName = orderItem.ProductName,
                Quantity = line.Quantity,
                UnitPrice = orderItem.UnitPrice
            });
        }

        return items;
    }

    public async Task<PagedResult<ReturnResponse>> GetForCustomerAsync(string businessId, string customerUserId, PageRequest page, CancellationToken ct = default)
    {
        var result = await returns.FindPagedAsync(
            r => r.BusinessId == businessId && r.CustomerUserId == customerUserId, page, r => r.CreatedAt, ct: ct);
        return result.Map(Map);
    }

    public async Task<PagedResult<ReturnResponse>> GetForBusinessAsync(string businessId, ReturnStatus? status, PageRequest page, CancellationToken ct = default)
    {
        var result = await returns.FindPagedAsync(
            r => r.BusinessId == businessId && (status == null || r.Status == status), page, r => r.CreatedAt, ct: ct);
        return result.Map(Map);
    }

    public async Task<ReturnResponse> GetByIdAsync(string tenantId, string businessId, string returnId, CancellationToken ct = default) =>
        Map(await GetScopedAsync(tenantId, businessId, returnId, ct));

    public async Task<ReturnResponse> DecideAsync(string tenantId, string businessId, string returnId, DecideReturnRequest request, string staffUserId, CancellationToken ct = default)
    {
        var entity = await GetScopedAsync(tenantId, businessId, returnId, ct);

        if (entity.Status != ReturnStatus.Requested)
        {
            throw new ConflictException($"A return in status '{entity.Status}' has already been decided.");
        }

        if (request.Approve)
        {
            var approved = request.ApprovedRefundAmount ?? entity.RequestedRefundAmount;
            if (approved < 0 || approved > entity.RequestedRefundAmount)
            {
                throw new ConflictException($"The approved refund must be between 0 and {entity.RequestedRefundAmount:0.00}.");
            }

            entity.ApprovedRefundAmount = approved;
            Transition(entity, ReturnStatus.Approved, request.Note, staffUserId);
        }
        else
        {
            Transition(entity, ReturnStatus.Rejected, request.Note, staffUserId);
        }

        await returns.UpdateAsync(entity, ct);
        await NotifyAsync(entity, b => EmailTemplates.ReturnDecision(b, entity, request.Note), ct);

        return Map(entity);
    }

    public async Task<ReturnResponse> MarkReceivedAsync(string tenantId, string businessId, string returnId, string staffUserId, CancellationToken ct = default)
    {
        var entity = await GetScopedAsync(tenantId, businessId, returnId, ct);

        if (entity.Status != ReturnStatus.Approved)
        {
            throw new ConflictException("Only an approved return can be marked received.");
        }

        // Restock happens here, on physical receipt — not on approval, and not on refund. This is
        // the step §9.21 identified as missing entirely: a refunded item used to vanish from stock.
        // Damaged goods are the exception: they come back but are not sellable.
        if (!entity.Restocked && entity.Reason != ReturnReason.Damaged)
        {
            foreach (var item in entity.Items)
            {
                await inventoryService.RecordMovementAsync(
                    entity.TenantId, entity.BusinessId, item.ProductId, StockMovementType.Return, item.Quantity,
                    $"Return {entity.RmaNumber} received", entity.OrderNumber, staffUserId, item.VariantId, ct);
            }

            entity.Restocked = true;
        }
        else if (entity.Reason == ReturnReason.Damaged)
        {
            foreach (var item in entity.Items)
            {
                await inventoryService.RecordMovementAsync(
                    entity.TenantId, entity.BusinessId, item.ProductId, StockMovementType.DamageWriteOff, 0,
                    $"Return {entity.RmaNumber} received damaged — not restocked", entity.OrderNumber, staffUserId, item.VariantId, ct);
            }
        }

        Transition(entity, ReturnStatus.Received, "Goods received.", staffUserId);
        await returns.UpdateAsync(entity, ct);

        return Map(entity);
    }

    public async Task<ReturnResponse> RefundAsync(string tenantId, string businessId, string returnId, string staffUserId, CancellationToken ct = default)
    {
        var entity = await GetScopedAsync(tenantId, businessId, returnId, ct);

        if (entity.Status != ReturnStatus.Received)
        {
            throw new ConflictException("Mark the goods received before refunding.");
        }

        var order = await orders.GetByIdAsync(entity.OrderId, ct)
            ?? throw new NotFoundException(nameof(Order), entity.OrderId);

        var amount = entity.ApprovedRefundAmount ?? entity.RequestedRefundAmount;

        // A partial Refund ledger entry — the thing the old whole-order-only model could not
        // represent at all. §9.16a's invariant still holds: a Revenue entry for this order was
        // written when it was delivered, so there is always something to offset.
        await ledgerEntries.AddAsync(new LedgerEntry
        {
            TenantId = entity.TenantId,
            BusinessId = entity.BusinessId,
            Type = LedgerEntryType.Refund,
            Amount = amount,
            Currency = entity.Currency,
            ReferenceOrderId = entity.OrderNumber
        }, ct);

        await SettleRefundAsync(entity, order, amount, ct);

        // Mark the returned quantities on the order itself, so a second return of the same lines
        // can't be requested and the order's own refunded total stays truthful.
        foreach (var item in entity.Items)
        {
            var orderItem = order.Items.FirstOrDefault(i => i.ProductId == item.ProductId && i.VariantId == item.VariantId);
            if (orderItem is not null)
            {
                orderItem.RefundedQuantity += item.Quantity;
            }
        }

        order.RefundedAmount += amount;

        // Only a return covering everything moves the order itself to Refunded. A partial return
        // leaves it Delivered, which is what it still is for the items the customer kept.
        if (order.Items.All(i => i.RefundedQuantity >= i.Quantity))
        {
            order.Status = OrderStatus.Refunded;
            order.StatusHistory.Add(new OrderStatusEvent
            {
                Status = OrderStatus.Refunded,
                Note = $"Fully refunded via return {entity.RmaNumber}."
            });
        }

        await orders.UpdateAsync(order, ct);

        entity.RefundedAt = DateTime.UtcNow;
        Transition(entity, ReturnStatus.Refunded, $"Refunded {amount:0.00} {entity.Currency}.", staffUserId);
        await returns.UpdateAsync(entity, ct);

        await NotifyAsync(entity, b => EmailTemplates.RefundIssued(b, entity, amount), ct);

        return Map(entity);
    }

    /// <summary>
    /// Where the money actually goes, per the resolution the customer chose. StoreCredit is
    /// settled immediately; a cash Refund has no gateway to call (§9.6), so it is recorded and
    /// left for staff to pay out by whatever means the business actually uses.
    /// </summary>
    private async Task SettleRefundAsync(ReturnRequest entity, Order order, decimal amount, CancellationToken ct)
    {
        if (entity.Resolution == ReturnResolution.StoreCredit)
        {
            await storeCreditService.RecordAsync(
                entity.TenantId, entity.BusinessId, entity.CustomerUserId, amount,
                StoreCreditReason.RefundToCredit, $"Return {entity.RmaNumber}", entity.OrderNumber, entity.Id, ct: ct);
            return;
        }

        // Value originally paid with a gift card goes back onto that card first — refunding it as
        // cash would let a customer convert store value into money.
        if (order.GiftCardsUsed.Count > 0)
        {
            var remaining = amount;
            var reversals = new List<OrderGiftCardUse>();

            foreach (var use in order.GiftCardsUsed)
            {
                if (remaining <= 0)
                {
                    break;
                }

                var portion = Math.Min(use.AmountApplied, remaining);
                remaining -= portion;
                reversals.Add(new OrderGiftCardUse
                {
                    GiftCardId = use.GiftCardId,
                    CodeSuffix = use.CodeSuffix,
                    AmountApplied = portion
                });
            }

            await giftCardService.RefundAsync(reversals, ct);
        }
    }

    public async Task<ReturnResponse> CancelAsync(string businessId, string customerUserId, string returnId, CancellationToken ct = default)
    {
        var entity = await returns.GetByIdAsync(returnId, ct);
        if (entity is null || entity.BusinessId != businessId || entity.CustomerUserId != customerUserId)
        {
            throw new NotFoundException(nameof(ReturnRequest), returnId);
        }

        if (entity.Status is not (ReturnStatus.Requested or ReturnStatus.Approved))
        {
            throw new ConflictException($"A return in status '{entity.Status}' can no longer be cancelled.");
        }

        Transition(entity, ReturnStatus.Cancelled, "Cancelled by customer.", customerUserId);
        await returns.UpdateAsync(entity, ct);
        return Map(entity);
    }

    private static void Transition(ReturnRequest entity, ReturnStatus status, string note, string? byUserId)
    {
        entity.Status = status;
        entity.StatusHistory.Add(new ReturnStatusEvent { Status = status, Note = note, ByUserId = byUserId });
    }

    private async Task NotifyAsync(ReturnRequest entity, Func<Business?, (string Subject, string PlainBody, string HtmlBody)> buildMessage, CancellationToken ct)
    {
        try
        {
            var order = await orders.GetByIdAsync(entity.OrderId, ct);
            if (!string.IsNullOrWhiteSpace(order?.ContactEmail))
            {
                var business = await businesses.GetByIdAsync(entity.BusinessId, ct);
                var (subject, plainBody, htmlBody) = buildMessage(business);
                await notificationService.NotifyAsync(new NotificationMessage(order.ContactEmail, subject, plainBody, htmlBody), ct);
            }
        }
        catch
        {
            // Best-effort, same rule as everywhere else: a notification must never fail the
            // operation that triggered it (§9.10).
        }
    }

    private async Task<ReturnRequest> GetScopedAsync(string tenantId, string businessId, string returnId, CancellationToken ct)
    {
        var entity = await returns.GetByIdAsync(returnId, ct);
        if (entity is null || entity.TenantId != tenantId || entity.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(ReturnRequest), returnId);
        }

        return entity;
    }

    private static string GenerateRmaNumber() =>
        $"RMA-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private static ReturnResponse Map(ReturnRequest r) => new(
        r.Id, r.RmaNumber, r.OrderId, r.OrderNumber, r.CustomerUserId,
        [.. r.Items.Select(i => new ReturnItemResponse(i.ProductId, i.VariantId, i.ProductName, i.Quantity, i.UnitPrice, i.LineRefund))],
        r.Reason, r.ReasonNote, r.Resolution, r.Status, r.RequestedRefundAmount, r.ApprovedRefundAmount,
        r.Currency, r.Restocked, r.RefundedAt,
        [.. r.StatusHistory.Select(e => new ReturnStatusEventResponse(e.Status, e.Timestamp, e.Note))],
        r.CreatedAt);
}

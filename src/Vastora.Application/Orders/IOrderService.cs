using Vastora.Application.Common;

namespace Vastora.Application.Orders;

public interface IOrderService
{
    /// <summary>
    /// Turns a cart into an Order. Rewritten in §9.17: every line is validated before any stock
    /// moves, each deduction is atomic and guarded against going negative, and a mid-flight
    /// failure compensates the deductions already applied instead of leaving them stranded.
    ///
    /// <paramref name="customerUserId"/> is null for a guest checkout (§9.27), in which case
    /// <paramref name="guestToken"/> identifies the cart and the request must carry a guest email.
    /// </summary>
    Task<OrderResponse> CheckoutAsync(
        string tenantId, string businessId, string? customerUserId, string? guestToken,
        CheckoutRequest request, CancellationToken ct = default);

    /// <summary>Prices the cart without committing anything — no stock moves, no coupon usage burnt (§9.19/§9.20/§9.23).</summary>
    Task<CheckoutPreviewResponse> PreviewAsync(
        string businessId, string? customerUserId, string? guestToken,
        CheckoutRequest request, CancellationToken ct = default);

    Task<PagedResult<OrderResponse>> GetForCustomerAsync(string businessId, string customerUserId, PageRequest page, CancellationToken ct = default);

    Task<PagedResult<OrderResponse>> GetForBusinessAsync(string tenantId, string businessId, OrderQuery query, CancellationToken ct = default);

    /// <summary>Orders currently assigned to a given DeliveryAgent within a Business.</summary>
    Task<PagedResult<OrderResponse>> GetAssignedToAgentAsync(string businessId, string deliveryAgentUserId, PageRequest page, CancellationToken ct = default);

    Task<OrderResponse> GetByIdForCustomerAsync(string businessId, string customerUserId, string orderId, CancellationToken ct = default);

    /// <summary>§9.27. A guest has no account to list orders under, so the order number plus the email that placed it is the lookup key.</summary>
    Task<OrderResponse> LookupGuestOrderAsync(string businessId, string orderNumber, string email, CancellationToken ct = default);

    Task<OrderResponse> GetByIdForBusinessAsync(string tenantId, string businessId, string orderId, CancellationToken ct = default);

    Task<OrderResponse> UpdateStatusAsync(string tenantId, string businessId, string orderId, UpdateOrderStatusRequest request, CancellationToken ct = default);

    /// <summary>Manual payment recording for the cash-on-delivery flow (§9.6) — no gateway exists to do this automatically yet.</summary>
    Task<OrderResponse> UpdatePaymentStatusAsync(string tenantId, string businessId, string orderId, UpdatePaymentStatusRequest request, CancellationToken ct = default);

    Task<OrderResponse> AssignDeliveryAgentAsync(string tenantId, string businessId, string orderId, AssignDeliveryAgentRequest request, CancellationToken ct = default);

    /// <summary>§9.20. Records an external courier shipment for a Business not using the delivery-agent module.</summary>
    Task<OrderResponse> UpdateShipmentAsync(string tenantId, string businessId, string orderId, UpdateShipmentRequest request, CancellationToken ct = default);

    Task<OrderResponse> UpdateInternalNoteAsync(string tenantId, string businessId, string orderId, UpdateOrderNoteRequest request, CancellationToken ct = default);

    /// <summary>§9.33. Assigns a gapless sequential invoice number on first call, then returns the same one.</summary>
    Task<InvoiceResponse> GetInvoiceAsync(string tenantId, string businessId, string orderId, CancellationToken ct = default);

    /// <summary>Customer-initiated cancellation; only allowed while the order hasn't shipped, restocks items.</summary>
    Task<OrderResponse> CancelAsync(string businessId, string customerUserId, string orderId, CancellationToken ct = default);
}

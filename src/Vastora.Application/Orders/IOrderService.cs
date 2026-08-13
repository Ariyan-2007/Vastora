namespace Vastora.Application.Orders;

public interface IOrderService
{
    /// <summary>Turns the customer's Cart into an Order: validates stock, decrements it, applies any coupon, clears the cart.</summary>
    Task<OrderResponse> CheckoutAsync(string tenantId, string businessId, string customerUserId, CheckoutRequest request, CancellationToken ct = default);

    Task<List<OrderResponse>> GetForCustomerAsync(string businessId, string customerUserId, CancellationToken ct = default);

    Task<List<OrderResponse>> GetForBusinessAsync(string tenantId, string businessId, CancellationToken ct = default);

    /// <summary>Orders currently assigned to a given DeliveryAgent within a Business.</summary>
    Task<List<OrderResponse>> GetAssignedToAgentAsync(string businessId, string deliveryAgentUserId, CancellationToken ct = default);

    Task<OrderResponse> GetByIdForCustomerAsync(string businessId, string customerUserId, string orderId, CancellationToken ct = default);

    Task<OrderResponse> GetByIdForBusinessAsync(string tenantId, string businessId, string orderId, CancellationToken ct = default);

    Task<OrderResponse> UpdateStatusAsync(string tenantId, string businessId, string orderId, UpdateOrderStatusRequest request, CancellationToken ct = default);

    Task<OrderResponse> AssignDeliveryAgentAsync(string tenantId, string businessId, string orderId, AssignDeliveryAgentRequest request, CancellationToken ct = default);

    /// <summary>Customer-initiated cancellation; only allowed while the order hasn't shipped, restocks items.</summary>
    Task<OrderResponse> CancelAsync(string businessId, string customerUserId, string orderId, CancellationToken ct = default);
}

namespace Vastora.Application.DeliveryAgents;

public interface IDeliveryAgentService
{
    Task<List<DeliveryAgentResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default);

    Task<DeliveryAgentResponse> GetByUserIdAsync(string businessId, string userId, CancellationToken ct = default);

    Task<DeliveryAgentResponse> UpdateStatusAsync(string tenantId, string businessId, string userId, UpdateDeliveryAgentStatusRequest request, CancellationToken ct = default);
}

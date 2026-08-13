using Vastora.Domain.Enums;

namespace Vastora.Application.DeliveryAgents;

public record DeliveryAgentResponse(
    string Id,
    string BusinessId,
    string UserId,
    DeliveryAgentStatus Status,
    int CompletedDeliveries,
    decimal DeliveryCharge,
    int LevelCode,
    decimal Balance);

public record UpdateDeliveryAgentStatusRequest(DeliveryAgentStatus Status);

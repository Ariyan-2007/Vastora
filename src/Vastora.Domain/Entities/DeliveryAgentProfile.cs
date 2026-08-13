using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>Operational stats for a user whose Role is DeliveryAgent, one profile per Business.</summary>
public class DeliveryAgentProfile : BaseEntity, ITenantScoped, IBusinessScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string BusinessId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DeliveryAgentStatus Status { get; set; } = DeliveryAgentStatus.Offline;

    public int CompletedDeliveries { get; set; }

    public decimal DeliveryCharge { get; set; } = 20m;

    public int LevelCode { get; set; } = 1;

    public decimal Balance { get; set; }
}

using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// The paying subscriber of Vastora. A TenantAccount owns one or more Businesses.
/// TenantType decides whether the owner only ever sees a single Business (SingleBusiness)
/// or gets a SuperOffice that spans every Business they run (MultiBusiness).
/// </summary>
public class TenantAccount : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public TenantType Type { get; set; } = TenantType.SingleBusiness;

    public TenantStatus Status { get; set; } = TenantStatus.PendingSetup;

    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Trial;

    public string OwnerUserId { get; set; } = string.Empty;

    public string ContactEmail { get; set; } = string.Empty;

    public string ContactPhone { get; set; } = string.Empty;
}

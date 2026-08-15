using Vastora.Domain.Common;
using Vastora.Domain.Enums;

namespace Vastora.Domain.Entities;

/// <summary>
/// One storefront (Landing Page + Shop + BackOffice) owned by a Tenant.
/// A SingleBusiness tenant has exactly one of these; a MultiBusiness tenant has several,
/// all visible together through that tenant's SuperOffice.
/// </summary>
public class Business : BaseEntity, ITenantScoped
{
    public string TenantId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public string? CustomDomain { get; set; }

    public string Description { get; set; } = string.Empty;

    public string LogoUrl { get; set; } = string.Empty;

    public string BannerUrl { get; set; } = string.Empty;

    public string ThemeColor { get; set; } = "#111827";

    public string Currency { get; set; } = "USD";

    public string Timezone { get; set; } = "UTC";

    public string ContactEmail { get; set; } = string.Empty;

    public string ContactPhone { get; set; } = string.Empty;

    public Address? Address { get; set; }

    public BusinessStatus Status { get; set; } = BusinessStatus.Draft;

    /// <summary>
    /// Whether this Business uses the DeliveryAgent workflow at all. Pickup-only sellers or
    /// ones using a third-party courier can turn it off; new DeliveryAgent staff can't be
    /// created and orders can't be assigned to one while disabled, but existing agents and
    /// in-flight assignments are left alone.
    /// </summary>
    public bool DeliveryModuleEnabled { get; set; } = true;
}

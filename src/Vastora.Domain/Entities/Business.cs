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
}

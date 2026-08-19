using Vastora.Domain.Enums;

namespace Vastora.Application.Businesses;

public interface IBusinessService
{
    /// <summary>Creates a Business under a Tenant. Rejected if the Tenant is SingleBusiness and already owns one.</summary>
    Task<BusinessResponse> CreateAsync(string tenantId, CreateBusinessRequest request, CancellationToken ct = default);

    /// <summary>SuperOffice / BackOffice lookup — scoped so a caller can never fetch another tenant's business.</summary>
    Task<BusinessResponse> GetByIdAsync(string tenantId, string businessId, CancellationToken ct = default);

    /// <summary>PlatformSuperAdmin lookup — no tenant scoping, any status.</summary>
    Task<BusinessResponse> GetByIdForPlatformAsync(string businessId, CancellationToken ct = default);

    /// <summary>Public storefront lookup by slug — no tenant scoping, only Active businesses are visible.</summary>
    Task<BusinessResponse> GetPublicBySlugAsync(string slug, CancellationToken ct = default);

    Task<List<BusinessResponse>> GetAllForTenantAsync(string tenantId, CancellationToken ct = default);

    Task<BusinessResponse> UpdateAsync(string tenantId, string businessId, UpdateBusinessRequest request, CancellationToken ct = default);

    /// <summary>Sets LogoUrl/BannerUrl only, so an upload doesn't force the caller to resend the whole Business (mirrors ProductService.AddImageAsync).</summary>
    Task<BusinessResponse> SetLogoAsync(string tenantId, string businessId, string logoUrl, CancellationToken ct = default);

    Task<BusinessResponse> SetBannerAsync(string tenantId, string businessId, string bannerUrl, CancellationToken ct = default);

    Task<BusinessResponse> UpdateStatusAsync(string tenantId, string businessId, BusinessStatus status, CancellationToken ct = default);

    /// <summary>Toggles whether this Business uses the DeliveryAgent workflow — see Business.DeliveryModuleEnabled.</summary>
    Task<BusinessResponse> UpdateDeliveryModuleAsync(string tenantId, string businessId, bool enabled, CancellationToken ct = default);

    /// <summary>SuperOffice only — a Business's own outbound mail identity. Never exposed through the regular BackOffice profile endpoints.</summary>
    Task<BusinessMailSettingsResponse> GetMailSettingsAsync(string tenantId, string businessId, CancellationToken ct = default);

    Task<BusinessMailSettingsResponse> UpdateMailSettingsAsync(string tenantId, string businessId, UpdateBusinessMailSettingsRequest request, CancellationToken ct = default);

    /// <summary>SuperOffice only — sets Business.ShopDomain/BackOfficeDomain, dynamically (§9.10 domain management).</summary>
    Task<BusinessResponse> UpdateDomainsAsync(string tenantId, string businessId, UpdateBusinessDomainsRequest request, CancellationToken ct = default);
}

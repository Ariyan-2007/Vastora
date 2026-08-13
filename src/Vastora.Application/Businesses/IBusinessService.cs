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

    Task<BusinessResponse> UpdateStatusAsync(string tenantId, string businessId, BusinessStatus status, CancellationToken ct = default);
}

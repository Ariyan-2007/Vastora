using Vastora.Domain.Enums;

namespace Vastora.Application.Tenants;

public interface ITenantService
{
    Task<TenantSignUpResponse> SignUpAsync(TenantSignUpRequest request, string ip, CancellationToken ct = default);

    /// <summary>Platform-level: every Tenant on Vastora.</summary>
    Task<List<TenantResponse>> GetAllAsync(CancellationToken ct = default);

    Task<TenantResponse> GetByIdAsync(string tenantId, CancellationToken ct = default);

    Task<TenantResponse> UpdateStatusAsync(string tenantId, TenantStatus status, CancellationToken ct = default);

    Task<TenantResponse> UpdatePlanAsync(string tenantId, SubscriptionPlan plan, CancellationToken ct = default);
}

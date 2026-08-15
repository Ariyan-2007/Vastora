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

    /// <summary>Single→MultiBusiness upgrade (or the reverse, if the Tenant owns at most one Business) — §9.4.</summary>
    Task<TenantResponse> UpdateTypeAsync(string tenantId, TenantType type, CancellationToken ct = default);

    /// <summary>Usage vs. plan limits (§9.9) — every Business under the Tenant, plus its staff/product counts.</summary>
    Task<TenantUsageResponse> GetUsageAsync(string tenantId, CancellationToken ct = default);
}

namespace Vastora.Application.Analytics;

public interface IAnalyticsService
{
    /// <summary>Revenue, order counts and top products across every Business a Tenant owns — §9.8.</summary>
    Task<TenantAnalyticsResponse> GetTenantAnalyticsAsync(string tenantId, CancellationToken ct = default);
}

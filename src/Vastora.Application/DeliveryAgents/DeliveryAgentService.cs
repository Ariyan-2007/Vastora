using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.DeliveryAgents;

public class DeliveryAgentService(IMongoRepository<DeliveryAgentProfile> profiles) : IDeliveryAgentService
{
    public async Task<List<DeliveryAgentResponse>> GetForBusinessAsync(string businessId, CancellationToken ct = default)
    {
        var list = await profiles.FindAsync(p => p.BusinessId == businessId, ct);
        return list.Select(Map).ToList();
    }

    public async Task<DeliveryAgentResponse> GetByUserIdAsync(string businessId, string userId, CancellationToken ct = default)
    {
        var profile = await GetScopedAsync(businessId, userId, ct);
        return Map(profile);
    }

    public async Task<DeliveryAgentResponse> UpdateStatusAsync(string tenantId, string businessId, string userId, UpdateDeliveryAgentStatusRequest request, CancellationToken ct = default)
    {
        var profile = await GetScopedAsync(businessId, userId, ct);
        if (profile.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(DeliveryAgentProfile), userId);
        }

        profile.Status = request.Status;
        profile.UpdatedAt = DateTime.UtcNow;
        await profiles.UpdateAsync(profile, ct);
        return Map(profile);
    }

    private async Task<DeliveryAgentProfile> GetScopedAsync(string businessId, string userId, CancellationToken ct)
    {
        var profile = await profiles.FindOneAsync(p => p.BusinessId == businessId && p.UserId == userId, ct);
        if (profile is null)
        {
            throw new NotFoundException(nameof(DeliveryAgentProfile), userId);
        }

        return profile;
    }

    private static DeliveryAgentResponse Map(DeliveryAgentProfile p) => new(
        p.Id, p.BusinessId, p.UserId, p.Status, p.CompletedDeliveries, p.DeliveryCharge, p.LevelCode, p.Balance);
}

using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;

namespace Vastora.Application.CustomerGroups;

public record CustomerGroupRequest(string Name, string Description, decimal? DiscountPercent, bool IsActive);

public record CustomerGroupResponse(
    string Id, string Name, string Description, decimal? DiscountPercent, bool IsActive, int MemberCount);

public record GroupMembershipRequest(List<string> CustomerUserIds);

/// <summary>§9.23. Named segments for targeted pricing and promotions.</summary>
public interface ICustomerGroupService
{
    Task<PagedResult<CustomerGroupResponse>> GetAllAsync(string businessId, PageRequest page, CancellationToken ct = default);

    Task<CustomerGroupResponse> CreateAsync(string tenantId, string businessId, CustomerGroupRequest request, CancellationToken ct = default);

    Task<CustomerGroupResponse> UpdateAsync(string tenantId, string businessId, string groupId, CustomerGroupRequest request, CancellationToken ct = default);

    Task DeleteAsync(string tenantId, string businessId, string groupId, CancellationToken ct = default);

    Task<CustomerGroupResponse> AddMembersAsync(string tenantId, string businessId, string groupId, GroupMembershipRequest request, CancellationToken ct = default);

    Task<CustomerGroupResponse> RemoveMembersAsync(string tenantId, string businessId, string groupId, GroupMembershipRequest request, CancellationToken ct = default);

    /// <summary>Groups a given customer belongs to. The authority for promotion targeting and group pricing.</summary>
    Task<List<CustomerGroup>> GetForCustomerAsync(string businessId, string customerUserId, CancellationToken ct = default);

    /// <summary>The best (largest) group discount available to a customer. Groups don't stack — the most generous wins.</summary>
    Task<decimal> GetBestDiscountPercentAsync(string businessId, string customerUserId, CancellationToken ct = default);
}

/// <inheritdoc cref="ICustomerGroupService"/>
public class CustomerGroupService(
    IMongoRepository<CustomerGroup> groups,
    IMongoRepository<AppUser> users) : ICustomerGroupService
{
    public async Task<PagedResult<CustomerGroupResponse>> GetAllAsync(string businessId, PageRequest page, CancellationToken ct = default)
    {
        var result = await groups.FindPagedAsync(g => g.BusinessId == businessId, page, g => g.Name, SortDirection.Ascending, ct);
        return result.Map(Map);
    }

    public async Task<CustomerGroupResponse> CreateAsync(string tenantId, string businessId, CustomerGroupRequest request, CancellationToken ct = default)
    {
        var group = new CustomerGroup
        {
            TenantId = tenantId,
            BusinessId = businessId,
            Name = request.Name,
            Description = request.Description,
            DiscountPercent = request.DiscountPercent,
            IsActive = request.IsActive
        };

        await groups.AddAsync(group, ct);
        return Map(group);
    }

    public async Task<CustomerGroupResponse> UpdateAsync(string tenantId, string businessId, string groupId, CustomerGroupRequest request, CancellationToken ct = default)
    {
        var group = await GetScopedAsync(tenantId, businessId, groupId, ct);

        group.Name = request.Name;
        group.Description = request.Description;
        group.DiscountPercent = request.DiscountPercent;
        group.IsActive = request.IsActive;

        await groups.UpdateAsync(group, ct);
        return Map(group);
    }

    public async Task DeleteAsync(string tenantId, string businessId, string groupId, CancellationToken ct = default)
    {
        var group = await GetScopedAsync(tenantId, businessId, groupId, ct);

        // Clear the cached ids off the members, otherwise a deleted group keeps granting its
        // discount through AppUser.CustomerGroupIds — the cache outliving its source.
        await SyncMemberCacheAsync(group.CustomerUserIds, groupId, add: false, ct);
        await groups.DeleteAsync(groupId, ct: ct);
    }

    public async Task<CustomerGroupResponse> AddMembersAsync(string tenantId, string businessId, string groupId, GroupMembershipRequest request, CancellationToken ct = default)
    {
        var group = await GetScopedAsync(tenantId, businessId, groupId, ct);

        var added = request.CustomerUserIds.Except(group.CustomerUserIds).ToList();
        group.CustomerUserIds.AddRange(added);
        await groups.UpdateAsync(group, ct);
        await SyncMemberCacheAsync(added, groupId, add: true, ct);

        return Map(group);
    }

    public async Task<CustomerGroupResponse> RemoveMembersAsync(string tenantId, string businessId, string groupId, GroupMembershipRequest request, CancellationToken ct = default)
    {
        var group = await GetScopedAsync(tenantId, businessId, groupId, ct);

        group.CustomerUserIds.RemoveAll(request.CustomerUserIds.Contains);
        await groups.UpdateAsync(group, ct);
        await SyncMemberCacheAsync(request.CustomerUserIds, groupId, add: false, ct);

        return Map(group);
    }

    public async Task<List<CustomerGroup>> GetForCustomerAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(customerUserId))
        {
            return [];
        }

        // Queried from the group side, not from AppUser.CustomerGroupIds — the group document is
        // the authority and the user field is only a cache.
        var all = await groups.FindAsync(g => g.BusinessId == businessId && g.IsActive, ct);
        return [.. all.Where(g => g.CustomerUserIds.Contains(customerUserId))];
    }

    public async Task<decimal> GetBestDiscountPercentAsync(string businessId, string customerUserId, CancellationToken ct = default)
    {
        var memberships = await GetForCustomerAsync(businessId, customerUserId, ct);
        return memberships
            .Where(g => g.DiscountPercent is > 0)
            .Select(g => g.DiscountPercent!.Value)
            .DefaultIfEmpty(0m)
            .Max();
    }

    private async Task SyncMemberCacheAsync(IEnumerable<string> userIds, string groupId, bool add, CancellationToken ct)
    {
        foreach (var userId in userIds)
        {
            var user = await users.GetByIdAsync(userId, ct);
            if (user is null)
            {
                continue;
            }

            var changed = add
                ? !user.CustomerGroupIds.Contains(groupId) && AddTo(user.CustomerGroupIds, groupId)
                : user.CustomerGroupIds.Remove(groupId);

            if (changed)
            {
                await users.UpdateAsync(user, ct);
            }
        }
    }

    private static bool AddTo(List<string> list, string value)
    {
        list.Add(value);
        return true;
    }

    private async Task<CustomerGroup> GetScopedAsync(string tenantId, string businessId, string groupId, CancellationToken ct)
    {
        var group = await groups.GetByIdAsync(groupId, ct);
        if (group is null || group.TenantId != tenantId || group.BusinessId != businessId)
        {
            throw new NotFoundException(nameof(CustomerGroup), groupId);
        }

        return group;
    }

    private static CustomerGroupResponse Map(CustomerGroup g) =>
        new(g.Id, g.Name, g.Description, g.DiscountPercent, g.IsActive, g.CustomerUserIds.Count);
}

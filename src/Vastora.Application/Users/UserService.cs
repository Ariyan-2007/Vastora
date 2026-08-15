using Vastora.Application.Auth;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Users;

public class UserService(
    IMongoRepository<AppUser> users,
    IMongoRepository<DeliveryAgentProfile> deliveryAgentProfiles,
    IMongoRepository<Business> businesses,
    IMongoRepository<TenantAccount> tenants,
    IPasswordHasher passwordHasher) : IUserService
{
    private static readonly UserRole[] CreatableStaffRoles =
        [UserRole.BusinessAdmin, UserRole.BusinessStaff, UserRole.DeliveryAgent];

    public async Task<UserSummaryResponse> CreateStaffAsync(string tenantId, string businessId, CreateStaffRequest request, CancellationToken ct = default)
    {
        if (!CreatableStaffRoles.Contains(request.Role))
        {
            throw new ForbiddenException($"Role '{request.Role}' cannot be created through this endpoint.");
        }

        if (request.Role == UserRole.DeliveryAgent)
        {
            var business = await businesses.GetByIdAsync(businessId, ct)
                ?? throw new NotFoundException(nameof(Business), businessId);
            if (!business.DeliveryModuleEnabled)
            {
                throw new ConflictException("Delivery module is disabled for this business.");
            }
        }

        var tenant = await tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(TenantAccount), tenantId);
        var limits = SubscriptionPlanLimits.For(tenant.Plan);
        if (limits.MaxStaffPerBusiness is int maxStaff)
        {
            var staffCount = await users.CountAsync(
                u => u.BusinessId == businessId
                     && (u.Role == UserRole.BusinessAdmin || u.Role == UserRole.BusinessStaff || u.Role == UserRole.DeliveryAgent),
                ct);
            if (staffCount >= maxStaff)
            {
                throw new ConflictException(
                    $"Your '{tenant.Plan}' plan allows up to {maxStaff} staff member(s) per Business. Upgrade your plan to add more.");
            }
        }

        var emailTaken = await users.ExistsAsync(u => u.Email == request.Email && u.Role != UserRole.Customer, ct);
        if (emailTaken)
        {
            throw new ConflictException($"An account with email '{request.Email}' already exists.");
        }

        var user = new AppUser
        {
            TenantId = tenantId,
            BusinessId = businessId,
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = passwordHasher.Hash(request.Password),
            Role = request.Role,
            Status = UserStatus.Active
        };
        await users.AddAsync(user, ct);

        if (request.Role == UserRole.DeliveryAgent)
        {
            await deliveryAgentProfiles.AddAsync(new DeliveryAgentProfile
            {
                TenantId = tenantId,
                BusinessId = businessId,
                UserId = user.Id
            }, ct);
        }

        return Map(user);
    }

    public async Task<List<UserSummaryResponse>> GetBusinessStaffAsync(string tenantId, string businessId, CancellationToken ct = default)
    {
        var list = await users.FindAsync(
            u => u.TenantId == tenantId && u.BusinessId == businessId
                 && (u.Role == UserRole.BusinessAdmin || u.Role == UserRole.BusinessStaff || u.Role == UserRole.DeliveryAgent),
            ct);
        return list.Select(Map).ToList();
    }

    public async Task<List<UserSummaryResponse>> GetBusinessCustomersAsync(string tenantId, string businessId, CancellationToken ct = default)
    {
        var list = await users.FindAsync(
            u => u.TenantId == tenantId && u.BusinessId == businessId && u.Role == UserRole.Customer, ct);
        return list.Select(Map).ToList();
    }

    public async Task<UserSummaryResponse> GetByIdAsync(string tenantId, string userId, CancellationToken ct = default)
    {
        var user = await GetScopedAsync(tenantId, userId, ct);
        return Map(user);
    }

    public async Task<UserSummaryResponse> GetMeAsync(string userId, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);
        return Map(user);
    }

    public async Task<UserSummaryResponse> UpdateProfileAsync(string userId, UpdateProfileRequest request, CancellationToken ct = default)
    {
        var user = await users.GetByIdAsync(userId, ct)
            ?? throw new NotFoundException(nameof(AppUser), userId);

        user.FullName = request.FullName;
        user.Phone = request.Phone;
        user.UpdatedAt = DateTime.UtcNow;
        await users.UpdateAsync(user, ct);
        return Map(user);
    }

    public async Task<UserSummaryResponse> UpdateStatusAsync(string tenantId, string userId, UserStatus status, CancellationToken ct = default)
    {
        var user = await GetScopedAsync(tenantId, userId, ct);
        user.Status = status;
        user.UpdatedAt = DateTime.UtcNow;
        await users.UpdateAsync(user, ct);
        return Map(user);
    }

    private async Task<AppUser> GetScopedAsync(string tenantId, string userId, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct);
        if (user is null || user.TenantId != tenantId)
        {
            throw new NotFoundException(nameof(AppUser), userId);
        }

        return user;
    }

    private static UserSummaryResponse Map(AppUser u) =>
        new(u.Id, u.FullName, u.Email, u.Role, u.TenantId, u.BusinessId, u.Status);
}

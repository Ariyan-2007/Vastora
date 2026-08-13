using Vastora.Application.Businesses;
using Vastora.Application.Common;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tenants;

public class TenantService(
    IMongoRepository<TenantAccount> tenants,
    IMongoRepository<AppUser> users,
    IPasswordHasher passwordHasher,
    IAuthTokenIssuer tokenIssuer,
    IBusinessService businessService) : ITenantService
{
    public async Task<TenantSignUpResponse> SignUpAsync(TenantSignUpRequest request, string ip, CancellationToken ct = default)
    {
        var emailTaken = await users.ExistsAsync(u => u.Email == request.OwnerEmail && u.Role != UserRole.Customer, ct);
        if (emailTaken)
        {
            throw new ConflictException($"An account with email '{request.OwnerEmail}' already exists.");
        }

        var slug = await GenerateUniqueTenantSlugAsync(request.TenantName, ct);

        var tenant = new TenantAccount
        {
            Name = request.TenantName,
            Slug = slug,
            Type = request.TenantType,
            Status = TenantStatus.Active,
            Plan = SubscriptionPlan.Trial,
            ContactEmail = request.OwnerEmail,
            ContactPhone = request.OwnerPhone
        };
        await tenants.AddAsync(tenant, ct);

        var owner = new AppUser
        {
            TenantId = tenant.Id,
            BusinessId = string.Empty,
            FullName = request.OwnerFullName,
            Email = request.OwnerEmail,
            Phone = request.OwnerPhone,
            PasswordHash = passwordHasher.Hash(request.OwnerPassword),
            Role = UserRole.TenantOwner,
            Status = UserStatus.Active
        };
        await users.AddAsync(owner, ct);

        tenant.OwnerUserId = owner.Id;
        tenant.UpdatedAt = DateTime.UtcNow;
        await tenants.UpdateAsync(tenant, ct);

        var business = await businessService.CreateAsync(
            tenant.Id,
            new CreateBusinessRequest(request.InitialBusinessName, request.InitialBusinessSlug, string.Empty, request.OwnerEmail, request.OwnerPhone),
            ct);

        var auth = await tokenIssuer.IssueAsync(owner, ip, ct);

        return new TenantSignUpResponse(Map(tenant), business, auth);
    }

    public async Task<List<TenantResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await tenants.GetAllAsync(ct);
        return list.Select(Map).ToList();
    }

    public async Task<TenantResponse> GetByIdAsync(string tenantId, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(TenantAccount), tenantId);
        return Map(tenant);
    }

    public async Task<TenantResponse> UpdateStatusAsync(string tenantId, TenantStatus status, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(TenantAccount), tenantId);
        tenant.Status = status;
        tenant.UpdatedAt = DateTime.UtcNow;
        await tenants.UpdateAsync(tenant, ct);
        return Map(tenant);
    }

    public async Task<TenantResponse> UpdatePlanAsync(string tenantId, SubscriptionPlan plan, CancellationToken ct = default)
    {
        var tenant = await tenants.GetByIdAsync(tenantId, ct)
            ?? throw new NotFoundException(nameof(TenantAccount), tenantId);
        tenant.Plan = plan;
        tenant.UpdatedAt = DateTime.UtcNow;
        await tenants.UpdateAsync(tenant, ct);
        return Map(tenant);
    }

    private async Task<string> GenerateUniqueTenantSlugAsync(string seed, CancellationToken ct)
    {
        var baseSlug = SlugHelper.Slugify(seed);
        var attempt = 0;
        while (true)
        {
            var candidate = SlugHelper.WithSuffix(baseSlug, attempt);
            var taken = await tenants.ExistsAsync(t => t.Slug == candidate, ct);
            if (!taken)
            {
                return candidate;
            }

            attempt++;
        }
    }

    private static TenantResponse Map(TenantAccount t) => new(
        t.Id, t.Name, t.Slug, t.Type, t.Status, t.Plan, t.OwnerUserId, t.ContactEmail, t.ContactPhone, t.CreatedAt);
}

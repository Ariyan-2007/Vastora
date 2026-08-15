using Vastora.Application.Common.Exceptions;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Application.Users;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Users;

public class UserServiceTests
{
    private static readonly IPasswordHasherFake PasswordHasher = new();

    private static (UserService Service, FakeMongoRepository<AppUser> Users, FakeMongoRepository<DeliveryAgentProfile> Profiles,
        FakeMongoRepository<Business> Businesses, FakeMongoRepository<TenantAccount> Tenants) Create()
    {
        var users = new FakeMongoRepository<AppUser>();
        var profiles = new FakeMongoRepository<DeliveryAgentProfile>();
        var businesses = new FakeMongoRepository<Business>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        var service = new UserService(users, profiles, businesses, tenants, PasswordHasher);
        return (service, users, profiles, businesses, tenants);
    }

    [Fact]
    public async Task CreateStaffAsync_DeliveryAgent_RejectedWhenModuleDisabled()
    {
        var (service, _, _, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        var business = businesses.Seed(new Business { TenantId = tenant.Id, DeliveryModuleEnabled = false })[0];

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateStaffAsync(tenant.Id, business.Id,
                new CreateStaffRequest("Agent", "a@b.com", "password123", "123", UserRole.DeliveryAgent), CancellationToken.None));
    }

    [Fact]
    public async Task CreateStaffAsync_DeliveryAgent_CreatesProfileWhenModuleEnabled()
    {
        var (service, users, profiles, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        var business = businesses.Seed(new Business { TenantId = tenant.Id, DeliveryModuleEnabled = true })[0];

        var result = await service.CreateStaffAsync(tenant.Id, business.Id,
            new CreateStaffRequest("Agent", "a@b.com", "password123", "123", UserRole.DeliveryAgent), CancellationToken.None);

        Assert.Equal(UserRole.DeliveryAgent, result.Role);
        Assert.Single(await profiles.FindAsync(p => p.UserId == result.Id, CancellationToken.None));
        Assert.Single(await users.FindAsync(u => u.Id == result.Id, CancellationToken.None));
    }

    [Fact]
    public async Task CreateStaffAsync_RejectsOnceBusinessHitsPlanStaffLimit()
    {
        var (service, users, _, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Trial })[0]; // limit: 3 staff/business
        var business = businesses.Seed(new Business { TenantId = tenant.Id })[0];
        for (var i = 0; i < 3; i++)
        {
            users.Seed(new AppUser { TenantId = tenant.Id, BusinessId = business.Id, Role = UserRole.BusinessStaff, Email = $"s{i}@b.com" });
        }

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateStaffAsync(tenant.Id, business.Id,
                new CreateStaffRequest("Fourth", "fourth@b.com", "password123", "123", UserRole.BusinessStaff), CancellationToken.None));

        Assert.Contains("Trial", ex.Message);
    }

    [Fact]
    public async Task CreateStaffAsync_RejectsNonStaffRole()
    {
        var (service, _, _, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Plan = SubscriptionPlan.Enterprise })[0];
        var business = businesses.Seed(new Business { TenantId = tenant.Id })[0];

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            service.CreateStaffAsync(tenant.Id, business.Id,
                new CreateStaffRequest("Cust", "c@b.com", "password123", "123", UserRole.Customer), CancellationToken.None));
    }

    private sealed class IPasswordHasherFake : Vastora.Application.Common.Interfaces.IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string hash) => hash == $"hashed:{password}";
    }
}

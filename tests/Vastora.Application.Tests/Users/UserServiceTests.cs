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

    [Fact]
    public async Task AddAddressAsync_FirstAddress_IsDefaultRegardlessOfRequest()
    {
        var (service, users, _, _, _) = Create();
        var user = users.Seed(new AppUser())[^1];

        var result = await service.AddAddressAsync(user.Id,
            new SaveAddressRequest("Home", "1 Main St", "", "Springfield", "", "00000", "US", "555", IsDefault: false),
            CancellationToken.None);

        Assert.True(result.IsDefault);
    }

    [Fact]
    public async Task AddAddressAsync_NewDefault_UnsetsPreviousDefault()
    {
        var (service, users, _, _, _) = Create();
        var user = users.Seed(new AppUser())[^1];
        await service.AddAddressAsync(user.Id,
            new SaveAddressRequest("Home", "1 Main St", "", "Springfield", "", "00000", "US", "555", IsDefault: true),
            CancellationToken.None);

        var second = await service.AddAddressAsync(user.Id,
            new SaveAddressRequest("Work", "2 Office Rd", "", "Springfield", "", "00001", "US", "555", IsDefault: true),
            CancellationToken.None);

        var all = await service.GetAddressesAsync(user.Id, CancellationToken.None);
        Assert.True(second.IsDefault);
        Assert.Single(all, a => a.IsDefault);
    }

    [Fact]
    public async Task DeleteAddressAsync_RemovingDefault_PromotesAnotherAddress()
    {
        var (service, users, _, _, _) = Create();
        var user = users.Seed(new AppUser())[^1];
        var first = await service.AddAddressAsync(user.Id,
            new SaveAddressRequest("Home", "1 Main St", "", "Springfield", "", "00000", "US", "555", IsDefault: true),
            CancellationToken.None);
        await service.AddAddressAsync(user.Id,
            new SaveAddressRequest("Work", "2 Office Rd", "", "Springfield", "", "00001", "US", "555", IsDefault: false),
            CancellationToken.None);

        await service.DeleteAddressAsync(user.Id, first.Id, CancellationToken.None);

        var remaining = await service.GetAddressesAsync(user.Id, CancellationToken.None);
        var onlyAddress = Assert.Single(remaining);
        Assert.True(onlyAddress.IsDefault);
    }

    [Fact]
    public async Task UpdateAddressAsync_UnknownId_ThrowsNotFound()
    {
        var (service, users, _, _, _) = Create();
        var user = users.Seed(new AppUser())[^1];

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateAddressAsync(user.Id, "missing",
            new SaveAddressRequest("Home", "1 Main St", "", "Springfield", "", "00000", "US", "555", IsDefault: false),
            CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAvatarAsync_ThenRemoveAvatarAsync_ClearsIt()
    {
        var (service, users, _, _, _) = Create();
        var user = users.Seed(new AppUser())[^1];

        var withAvatar = await service.UpdateAvatarAsync(user.Id, "/uploads/biz/avatar.png", CancellationToken.None);
        Assert.Equal("/uploads/biz/avatar.png", withAvatar.AvatarUrl);

        var cleared = await service.RemoveAvatarAsync(user.Id, CancellationToken.None);
        Assert.Equal(string.Empty, cleared.AvatarUrl);
    }

    private sealed class IPasswordHasherFake : Vastora.Application.Common.Interfaces.IPasswordHasher
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string hash) => hash == $"hashed:{password}";
    }
}

using Vastora.Application.Businesses;
using Vastora.Application.Common.Exceptions;
using Vastora.Application.Tests.TestDoubles;
using Vastora.Domain.Entities;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Businesses;

public class BusinessServiceTests
{
    private static (BusinessService Service, FakeMongoRepository<Business> Businesses, FakeMongoRepository<TenantAccount> Tenants) Create()
    {
        var businesses = new FakeMongoRepository<Business>();
        var tenants = new FakeMongoRepository<TenantAccount>();
        return (new BusinessService(businesses, tenants), businesses, tenants);
    }

    [Fact]
    public async Task CreateAsync_SingleBusinessTenant_RejectsSecondBusiness()
    {
        var (service, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Type = TenantType.SingleBusiness, Plan = SubscriptionPlan.Enterprise })[0];
        businesses.Seed(new Business { TenantId = tenant.Id, Name = "First", Slug = "first" });

        await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(tenant.Id, new CreateBusinessRequest("Second", null, "", "a@b.com", "123"), CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_MultiBusinessTenant_OnTrialPlan_CappedAtOneByPlanNotType()
    {
        var (service, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Type = TenantType.MultiBusiness, Plan = SubscriptionPlan.Trial })[0];
        businesses.Seed(new Business { TenantId = tenant.Id, Name = "First", Slug = "first" });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            service.CreateAsync(tenant.Id, new CreateBusinessRequest("Second", null, "", "a@b.com", "123"), CancellationToken.None));

        Assert.Contains("Trial", ex.Message);
    }

    [Fact]
    public async Task CreateAsync_MultiBusinessTenant_OnGrowthPlan_AllowsUpToFive()
    {
        var (service, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount { Type = TenantType.MultiBusiness, Plan = SubscriptionPlan.Growth })[0];
        for (var i = 0; i < 4; i++)
        {
            businesses.Seed(new Business { TenantId = tenant.Id, Name = $"Biz {i}", Slug = $"biz-{i}" });
        }

        var result = await service.CreateAsync(tenant.Id, new CreateBusinessRequest("Fifth", null, "", "a@b.com", "123"), CancellationToken.None);

        Assert.Equal("Fifth", result.Name);
    }

    [Fact]
    public async Task UpdateDeliveryModuleAsync_TogglesFlag()
    {
        var (service, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount())[0];
        var business = businesses.Seed(new Business { TenantId = tenant.Id, Name = "Biz", Slug = "biz" })[0];
        Assert.True(business.DeliveryModuleEnabled);

        var result = await service.UpdateDeliveryModuleAsync(tenant.Id, business.Id, false, CancellationToken.None);

        Assert.False(result.DeliveryModuleEnabled);
    }

    [Fact]
    public async Task SetLogoAsync_SetsOnlyTheLogo_LeavingOtherFieldsUntouched()
    {
        var (service, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount())[0];
        var business = businesses.Seed(new Business { TenantId = tenant.Id, Name = "Biz", Slug = "biz", BannerUrl = "/uploads/biz/old-banner.jpg" })[0];

        var result = await service.SetLogoAsync(tenant.Id, business.Id, "/uploads/biz/new-logo.jpg", CancellationToken.None);

        Assert.Equal("/uploads/biz/new-logo.jpg", result.LogoUrl);
        Assert.Equal("/uploads/biz/old-banner.jpg", result.BannerUrl);
    }

    [Fact]
    public async Task SetBannerAsync_SetsOnlyTheBanner_LeavingOtherFieldsUntouched()
    {
        var (service, businesses, tenants) = Create();
        var tenant = tenants.Seed(new TenantAccount())[0];
        var business = businesses.Seed(new Business { TenantId = tenant.Id, Name = "Biz", Slug = "biz", LogoUrl = "/uploads/biz/old-logo.jpg" })[0];

        var result = await service.SetBannerAsync(tenant.Id, business.Id, "/uploads/biz/new-banner.jpg", CancellationToken.None);

        Assert.Equal("/uploads/biz/new-banner.jpg", result.BannerUrl);
        Assert.Equal("/uploads/biz/old-logo.jpg", result.LogoUrl);
    }
}

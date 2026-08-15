using Vastora.Application.Tenants;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tests.Tenants;

public class SubscriptionPlanLimitsTests
{
    [Theory]
    [InlineData(SubscriptionPlan.Trial, 1, 3, 20)]
    [InlineData(SubscriptionPlan.Starter, 1, 10, 200)]
    [InlineData(SubscriptionPlan.Growth, 5, 50, 2000)]
    public void For_ReturnsExpectedFiniteLimits(SubscriptionPlan plan, int maxBusinesses, int maxStaff, int maxProducts)
    {
        var limits = SubscriptionPlanLimits.For(plan);

        Assert.Equal(maxBusinesses, limits.MaxBusinesses);
        Assert.Equal(maxStaff, limits.MaxStaffPerBusiness);
        Assert.Equal(maxProducts, limits.MaxProductsPerBusiness);
    }

    [Fact]
    public void For_Enterprise_IsUnlimited()
    {
        var limits = SubscriptionPlanLimits.For(SubscriptionPlan.Enterprise);

        Assert.Null(limits.MaxBusinesses);
        Assert.Null(limits.MaxStaffPerBusiness);
        Assert.Null(limits.MaxProductsPerBusiness);
    }
}

using Vastora.Domain.Enums;

namespace Vastora.Application.Tenants;

/// <summary>Feature-gating limits for one SubscriptionPlan tier. Null means unlimited.</summary>
public sealed record PlanLimits(int? MaxBusinesses, int? MaxStaffPerBusiness, int? MaxProductsPerBusiness);

/// <summary>
/// Per-plan limits enforced at creation time by BusinessService, UserService (staff) and
/// ProductService — see Roadmap §9.9. Kept as static, in-code data rather than a stored
/// entity: there's no admin UI to configure limits per plan yet, and re-shaping this into a
/// database-backed table is easy to do later if that need shows up (see §9.9's own note).
/// </summary>
public static class SubscriptionPlanLimits
{
    public static PlanLimits For(SubscriptionPlan plan) => plan switch
    {
        SubscriptionPlan.Trial => new PlanLimits(MaxBusinesses: 1, MaxStaffPerBusiness: 3, MaxProductsPerBusiness: 20),
        SubscriptionPlan.Starter => new PlanLimits(MaxBusinesses: 1, MaxStaffPerBusiness: 10, MaxProductsPerBusiness: 200),
        SubscriptionPlan.Growth => new PlanLimits(MaxBusinesses: 5, MaxStaffPerBusiness: 50, MaxProductsPerBusiness: 2000),
        SubscriptionPlan.Enterprise => new PlanLimits(MaxBusinesses: null, MaxStaffPerBusiness: null, MaxProductsPerBusiness: null),
        _ => new PlanLimits(MaxBusinesses: 1, MaxStaffPerBusiness: 3, MaxProductsPerBusiness: 20)
    };
}

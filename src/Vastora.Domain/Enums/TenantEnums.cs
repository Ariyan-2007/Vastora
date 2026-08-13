namespace Vastora.Domain.Enums;

/// <summary>
/// A Tenant that runs exactly one Business gets Landing Page + Shop + BackOffice.
/// A Tenant that runs several Businesses under one owner additionally gets a SuperOffice
/// that can see and control every Business it owns.
/// </summary>
public enum TenantType
{
    SingleBusiness = 1,
    MultiBusiness = 2
}

public enum TenantStatus
{
    PendingSetup = 1,
    Active = 2,
    Suspended = 3,
    Cancelled = 4
}

/// <summary>
/// Subscription tier purchased from Vastora. Drives feature flags / limits later on;
/// kept simple for now so billing can be layered on without reshaping the model.
/// </summary>
public enum SubscriptionPlan
{
    Trial = 1,
    Starter = 2,
    Growth = 3,
    Enterprise = 4
}

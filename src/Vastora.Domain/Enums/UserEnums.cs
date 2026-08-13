namespace Vastora.Domain.Enums;

/// <summary>
/// Role hierarchy, broadest to narrowest:
///   PlatformSuperAdmin - Vastora's own staff. Manages every Tenant on the platform.
///   TenantOwner        - Owns the subscription. Has SuperOffice access across every
///                         Business under their Tenant (meaningful once TenantType is MultiBusiness).
///   BusinessAdmin       - Full BackOffice access, scoped to a single Business.
///   BusinessStaff       - Limited BackOffice access, scoped to a single Business.
///   DeliveryAgent       - Fulfils orders for a single Business.
///   Customer            - Shops on a single Business's storefront.
/// </summary>
public enum UserRole
{
    PlatformSuperAdmin = 1,
    TenantOwner = 2,
    BusinessAdmin = 3,
    BusinessStaff = 4,
    DeliveryAgent = 5,
    Customer = 6
}

public enum UserStatus
{
    PendingVerification = 1,
    Active = 2,
    Blocked = 3
}

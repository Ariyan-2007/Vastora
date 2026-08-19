using Vastora.Application.Auth;
using Vastora.Application.Businesses;
using Vastora.Domain.Enums;

namespace Vastora.Application.Tenants;

public record TenantResponse(
    string Id,
    string Name,
    string Slug,
    TenantType Type,
    TenantStatus Status,
    SubscriptionPlan Plan,
    string OwnerUserId,
    string ContactEmail,
    string ContactPhone,
    string? SuperOfficeDomain,
    DateTime CreatedAt);

/// <summary>
/// One call provisions the whole account: the Tenant, its owner login, and the first Business
/// (Landing Page + Shop + BackOffice). TenantType.MultiBusiness owners can add further
/// Businesses afterwards through IBusinessService, unlocking their SuperOffice view.
/// </summary>
public record TenantSignUpRequest(
    string TenantName,
    TenantType TenantType,
    string OwnerFullName,
    string OwnerEmail,
    string OwnerPassword,
    string OwnerPhone,
    string InitialBusinessName,
    string? InitialBusinessSlug);

public record TenantSignUpResponse(TenantResponse Tenant, BusinessResponse Business, AuthResponse Auth);

public record UpdateTenantStatusRequest(TenantStatus Status);

public record UpdateTenantPlanRequest(SubscriptionPlan Plan);

public record UpdateTenantTypeRequest(TenantType Type);

/// <summary>
/// Platform-only — not a TenantOwner self-service field. Sets the address this Tenant's SuperOffice
/// is reachable at, so a TenantOwner's own password-reset link resolves correctly (see
/// AuthService.ResolveLinkBase). Kept out of TenantOwner's own reach deliberately: if their account
/// is the one locked out, Platform is who needs to be able to fix where the reset link points.
/// </summary>
public record UpdateTenantSuperOfficeDomainRequest(string? SuperOfficeDomain);

/// <summary>Current usage vs. the plan's per-Business limits (§9.9) for one Business under a Tenant.</summary>
public record BusinessUsageResponse(
    string BusinessId,
    string BusinessName,
    int StaffCount,
    int? MaxStaffPerBusiness,
    int ProductCount,
    int? MaxProductsPerBusiness);

/// <summary>Tenant-wide usage vs. SubscriptionPlanLimits — null limits mean unlimited (Enterprise).</summary>
public record TenantUsageResponse(
    SubscriptionPlan Plan,
    int BusinessCount,
    int? MaxBusinesses,
    List<BusinessUsageResponse> Businesses);

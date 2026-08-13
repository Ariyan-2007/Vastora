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

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>Vastora's own staff console — every Tenant on the platform.</summary>
[Tags("Platform")]
[Route("api/platform/tenants")]
[Authorize(Roles = nameof(UserRole.PlatformSuperAdmin))]
public class PlatformController(
    ICurrentUserContext currentUser,
    ITenantService tenantService,
    IBusinessService businessService,
    IBusinessWipeService businessWipeService) : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<TenantResponse>>> GetAll(CancellationToken ct)
    {
        var result = await tenantService.GetAllAsync(ct);
        return Ok(result);
    }

    /// <summary>
    /// Platform-initiated onboarding — the exact same provisioning as the public self-serve
    /// `POST /api/tenants/signup` (Tenant + TenantOwner login + first Business, one call), just
    /// triggered by Vastora staff instead of the customer. For a deal closed out-of-band (a sales
    /// call, a signed contract) where the account needs to exist before the customer ever touches
    /// a signup page.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TenantSignUpResponse>> CreateTenant(TenantSignUpRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await tenantService.SignUpAsync(request, ip, ct);
        return Ok(result);
    }

    [HttpGet("{tenantId}")]
    public async Task<ActionResult<TenantResponse>> GetById(string tenantId, CancellationToken ct)
    {
        var result = await tenantService.GetByIdAsync(tenantId, ct);
        return Ok(result);
    }

    [HttpPatch("{tenantId}/status")]
    public async Task<ActionResult<TenantResponse>> UpdateStatus(string tenantId, UpdateTenantStatusRequest request, CancellationToken ct)
    {
        var result = await tenantService.UpdateStatusAsync(tenantId, request.Status, ct);
        return Ok(result);
    }

    [HttpPatch("{tenantId}/plan")]
    public async Task<ActionResult<TenantResponse>> UpdatePlan(string tenantId, UpdateTenantPlanRequest request, CancellationToken ct)
    {
        var result = await tenantService.UpdatePlanAsync(tenantId, request.Plan, ct);
        return Ok(result);
    }

    /// <summary>Any Tenant's usage vs. its plan limits (§9.9) — same shape as TenantOwner's own `GET /api/tenants/me/usage`.</summary>
    [HttpGet("{tenantId}/usage")]
    public async Task<ActionResult<TenantUsageResponse>> GetUsage(string tenantId, CancellationToken ct)
    {
        var result = await tenantService.GetUsageAsync(tenantId, ct);
        return Ok(result);
    }

    /// <summary>Single↔MultiBusiness account-shape change (§9.4) — Platform-only, no self-serve flow yet.</summary>
    [HttpPatch("{tenantId}/type")]
    public async Task<ActionResult<TenantResponse>> UpdateType(string tenantId, UpdateTenantTypeRequest request, CancellationToken ct)
    {
        var result = await tenantService.UpdateTypeAsync(tenantId, request.Type, ct);
        return Ok(result);
    }

    /// <summary>Every Business under this Tenant, any status — the same list a TenantOwner sees in SuperOffice.</summary>
    [HttpGet("{tenantId}/businesses")]
    public async Task<ActionResult<List<BusinessResponse>>> GetBusinesses(string tenantId, CancellationToken ct)
    {
        var result = await businessService.GetAllForTenantAsync(tenantId, ct);
        return Ok(result);
    }

    /// <summary>
    /// Adds a Business to any Tenant — the first Business, or an additional one for a
    /// MultiBusiness tenant. Goes through the same rules as the TenantOwner's own SuperOffice
    /// "add business" flow (SingleBusiness cap, SubscriptionPlanLimits.MaxBusinesses).
    /// </summary>
    [HttpPost("{tenantId}/businesses")]
    public async Task<ActionResult<BusinessResponse>> CreateBusiness(string tenantId, CreateBusinessRequest request, CancellationToken ct)
    {
        var result = await businessService.CreateAsync(tenantId, request, ct);
        return Ok(result);
    }

    /// <summary>Draft/Active/Suspended — "shut down" is Suspended. Same operation SuperOffice already exposes to TenantOwner, just unscoped from any single tenant.</summary>
    [HttpPatch("{tenantId}/businesses/{businessId}/status")]
    public async Task<ActionResult<BusinessResponse>> UpdateBusinessStatus(string tenantId, string businessId, [FromBody] BusinessStatus status, CancellationToken ct)
    {
        var result = await businessService.UpdateStatusAsync(tenantId, businessId, status, ct);
        return Ok(result);
    }

    /// <summary>
    /// Irreversible-in-practice: soft-deletes the Business and everything under it (products,
    /// orders, staff, coupons, the lot — see BusinessWipeService for the exact scope). Requires
    /// the caller to echo the Business's slug in the body as a confirmation gate.
    /// </summary>
    [HttpPost("{tenantId}/businesses/{businessId}/wipe")]
    public async Task<IActionResult> WipeBusiness(string tenantId, string businessId, WipeBusinessRequest request, CancellationToken ct)
    {
        await businessWipeService.WipeAsync(tenantId, businessId, request.ConfirmSlug, CurrentUser.UserId, ct);
        return NoContent();
    }
}

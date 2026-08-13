using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>Vastora's own staff console — every Tenant on the platform.</summary>
[Tags("Platform")]
[Route("api/platform/tenants")]
[Authorize(Roles = nameof(UserRole.PlatformSuperAdmin))]
public class PlatformController(ICurrentUserContext currentUser, ITenantService tenantService) : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<List<TenantResponse>>> GetAll(CancellationToken ct)
    {
        var result = await tenantService.GetAllAsync(ct);
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
}

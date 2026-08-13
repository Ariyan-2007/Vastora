using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Common.Interfaces;
using Vastora.Application.Tenants;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>Onboarding: one call provisions a Tenant, its owner login and its first Business.</summary>
[Tags("Tenant Onboarding")]
[Route("api/tenants")]
public class TenantsController(ICurrentUserContext currentUser, ITenantService tenantService) : VastoraControllerBase(currentUser)
{
    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<ActionResult<TenantSignUpResponse>> SignUp(TenantSignUpRequest request, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = await tenantService.SignUpAsync(request, ip, ct);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize(Roles = nameof(UserRole.TenantOwner))]
    public async Task<ActionResult<TenantResponse>> GetMyTenant(CancellationToken ct)
    {
        var result = await tenantService.GetByIdAsync(CurrentUser.TenantId, ct);
        return Ok(result);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Vastora.Application.Businesses;
using Vastora.Application.Common.Interfaces;
using Vastora.Domain.Enums;

namespace Vastora.API.Controllers;

/// <summary>BackOffice self-management of one Business's profile, reachable by anyone scoped to it.</summary>
[Tags("BackOffice - Business")]
[Route("api/businesses/{businessId}")]
[Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)},{nameof(UserRole.BusinessStaff)}")]
[Authorize(Policy = "BusinessMember")]
public class BusinessesController(ICurrentUserContext currentUser, IBusinessService businessService)
    : VastoraControllerBase(currentUser)
{
    [HttpGet]
    public async Task<ActionResult<BusinessResponse>> GetById(string businessId, CancellationToken ct)
    {
        var result = await businessService.GetByIdForPlatformAsync(businessId, ct);
        return Ok(result);
    }

    [HttpPut]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<ActionResult<BusinessResponse>> Update(string businessId, UpdateBusinessRequest request, CancellationToken ct)
    {
        var result = await businessService.UpdateAsync(ResolvedTenantId, businessId, request, ct);
        return Ok(result);
    }

    /// <summary>Turns the DeliveryAgent workflow on/off for this Business — see Roadmap §9.14.</summary>
    [HttpPatch("delivery-module")]
    [Authorize(Roles = $"{nameof(UserRole.PlatformSuperAdmin)},{nameof(UserRole.TenantOwner)},{nameof(UserRole.BusinessAdmin)}")]
    public async Task<ActionResult<BusinessResponse>> UpdateDeliveryModule(string businessId, UpdateDeliveryModuleRequest request, CancellationToken ct)
    {
        var result = await businessService.UpdateDeliveryModuleAsync(ResolvedTenantId, businessId, request.Enabled, ct);
        return Ok(result);
    }
}
